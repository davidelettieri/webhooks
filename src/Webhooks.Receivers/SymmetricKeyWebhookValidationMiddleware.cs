using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Webhooks.Receivers;

public class SymmetricKeyWebhookValidationMiddleware(
    ILogger<SymmetricKeyWebhookValidationMiddleware> logger,
    TimeProvider timeProvider,
    IValidationWebhookKeyRetriever keyRetriever,
    RequestDelegate next)
{
    private readonly ILogger<SymmetricKeyWebhookValidationMiddleware> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IValidationWebhookKeyRetriever _keyRetriever = keyRetriever;
    private readonly RequestDelegate _next = next;

    // Keep a strict, safe encoding instance if ever needed for text parts
    private static readonly UTF8Encoding SafeUtf8Encoding =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Header names
    private const string IdHeaderKey = "webhook-id";
    private const string SignatureHeaderKey = "webhook-signature";

    // Time window tolerance (anti-replay window)
    private const int ToleranceInSeconds = 60 * 5;

    // DoS controls
    private const int MaxPayloadBytes = 256 * 1024; // 256KB max request body
    private const int BufferThresholdBytes = 64 * 1024; // move to disk buffering after 64KB if needed
    private const int MaxSignatureTokens = 5; // cap number of signature values checked
    private const int ExpectedSignatureBytes = 32; // HMAC-SHA256
    private const int MaxBase64SignatureChars = 88; // generous upper bound for 32-byte base64

    // Reasonable cap on msgId length to avoid pathological header values
    private const int MaxMessageIdChars = 256;

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var ct = context.RequestAborted;

        // Extract and validate required headers first (cheapest checks).
        var headers = request.Headers;

        string? msgId = headers.TryGetValue(IdHeaderKey, out var unbrandedId) ? unbrandedId.ToString() : null;
        string? msgSignature = headers.TryGetValue(SignatureHeaderKey, out var signatureHeader)
            ? signatureHeader.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(msgSignature))
        {
            _logger.LogWarning("Webhook rejected: missing required headers.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (msgId.Length > MaxMessageIdChars)
        {
            _logger.LogWarning("Webhook rejected: message id too large.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Parse signature header (key=value CSV) and extract timestamp/signatures
        if (!TryParseSignatureHeader(msgSignature, out var parsed))
        {
            _logger.LogWarning("Webhook rejected: malformed signature header.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (parsed.TimestampUnixSeconds is not { } ts || parsed.V1Base64.Count == 0)
        {
            _logger.LogWarning("Webhook rejected: signature header missing required fields.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Validate timestamp tolerance using the provided 't' value
        if (!VerifyTimestamp(ts.ToString(CultureInfo.InvariantCulture), out var timestamp))
        {
            _logger.LogWarning("Webhook rejected: invalid or out-of-tolerance timestamp.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Get signing key early; don't spend cycles if we cannot verify.
        var key = _keyRetriever.GetKey(context);
        if (key.Length == 0)
        {
            _logger.LogWarning("Webhook rejected: no signing key available.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Short-circuit on known-too-large bodies
        if (request.ContentLength is { } contentLength and > MaxPayloadBytes)
        {
            _logger.LogWarning("Webhook rejected: payload too large. Content-Length={Length}", contentLength);
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        // Enable buffering with a strict upper limit to prevent unbounded memory usage.
        // If body exceeds MaxPayloadBytes, a read will either exceed our own guard or throw.
        request.EnableBuffering(BufferThresholdBytes, MaxPayloadBytes);
        request.Body.Position = 0;

        // Read body with a hard byte limit and minimal allocations.
        byte[]? payloadBytes;
        try

        {
            payloadBytes = await ReadBodyWithLimitAsync(request, MaxPayloadBytes, ct);
        }
        catch
            (OperationCanceledException)
        {
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return;
        }
        catch (IOException)
        {
            // Likely due to max buffer exceeded in server buffering layer.
            _logger.LogWarning("Webhook rejected: payload exceeded buffering limits.");
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        catch
        {
            _logger.LogWarning("Webhook rejected: failed to read request body.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (payloadBytes is null || payloadBytes.Length == 0)
        {
            _logger.LogWarning("Webhook rejected: payload missing or too large.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Rewind body for downstream consumers.
        request.Body.Position = 0;

        // Compute expected signature over raw bytes without concatenating large strings.
        var expectedSignature = Sign(key, msgId, timestamp.Value, payloadBytes);

        // Enforce signature count cap
        if (parsed.V1Base64.Count > MaxSignatureTokens)
        {
            _logger.LogWarning("Webhook rejected: too many signature entries.");
            CryptographicOperations.ZeroMemory(expectedSignature);
            Array.Clear(payloadBytes, 0, payloadBytes.Length);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        foreach (var b64 in parsed.V1Base64)
        {
            if (string.IsNullOrEmpty(b64) || b64.Length > MaxBase64SignatureChars)
            {
                continue;
            }

            if (!TryDecodeBase64OrBase64Url(b64, out var providedSigBytes))
            {
                continue;
            }

            var match = providedSigBytes.Length == ExpectedSignatureBytes &&
                        CryptographicOperations.FixedTimeEquals(expectedSignature, providedSigBytes);

            CryptographicOperations.ZeroMemory(providedSigBytes);

            if (match)
            {
                // Best-effort scrub before continuing.
                CryptographicOperations.ZeroMemory(expectedSignature);
                Array.Clear(payloadBytes, 0, payloadBytes.Length);
                context.SetWebhookHeader(new WebhookHeader(msgId, timestamp.Value));
                await _next(context);
                return;
            }
        }

        _logger.LogWarning("Webhook rejected: signature validation failed.");
        CryptographicOperations.ZeroMemory(expectedSignature);
        Array.Clear(payloadBytes, 0, payloadBytes.Length);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private bool VerifyTimestamp(string timestampHeader, [NotNullWhen(true)] out DateTimeOffset? timestamp)
    {
        var now = _timeProvider.GetUtcNow();

        try
        {
            var timestampInt = long.Parse(timestampHeader, CultureInfo.InvariantCulture);
            timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampInt);
        }
        catch
        {
            timestamp = null;
            return false;
        }

        if (timestamp < now.AddSeconds(-1 * ToleranceInSeconds) || timestamp > now.AddSeconds(ToleranceInSeconds))
        {
            timestamp = null;
            return false;
        }

        return true;
    }

    private static byte[] Sign(byte[] key, string msgId, DateTimeOffset timestamp, ReadOnlySpan<byte> payloadBytes)
    {
        // Build prefix "msgId.timestamp." as UTF-8 and hash with the payload without extra copies.
        var prefix = $"{msgId}.{timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}.";

        // Encode prefix with minimal allocation: rent a small buffer when possible; otherwise allocate.
        byte[]? rented = null;
        byte[] prefixBytes;
        int prefixByteCount = SafeUtf8Encoding.GetByteCount(prefix);
        if (prefixByteCount <= 512)
        {
            prefixBytes = ArrayPool<byte>.Shared.Rent(prefixByteCount);
            rented = prefixBytes;
            prefixByteCount = SafeUtf8Encoding.GetBytes(prefix, 0, prefix.Length, prefixBytes, 0);
        }
        else
        {
            prefixBytes = SafeUtf8Encoding.GetBytes(prefix);
        }

        try
        {
            // Use IncrementalHash to append multiple segments without copying the payload span.
            using var ih = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
            ih.AppendData(prefixBytes, 0, prefixByteCount);
            ih.AppendData(payloadBytes);
            return ih.GetHashAndReset();
        }
        finally
        {
            if (rented is not null)
            {
                CryptographicOperations.ZeroMemory(rented.AsSpan(0, prefixByteCount));
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            }
            else
            {
                // Best-effort scrub allocated prefix bytes.
                CryptographicOperations.ZeroMemory(prefixBytes);
            }
        }
    }

// Robust parser for the signature header using key=value CSV only.
// Expected keys: t (unix timestamp seconds), v1 (one or more entries), k/kid (optional key id).
    private static bool TryParseSignatureHeader(string headerValue, out SignatureHeaderComponents result)
    {
        result = new SignatureHeaderComponents();
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parts = headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 10)
        {
            return false; // cap total parts to avoid header-inflation DoS
        }

        foreach (var p in parts)
        {
            var idx = p.IndexOf('=');
            if (idx <= 0 || idx == p.Length - 1)
            {
                continue;
            }

            var key = p.AsSpan(0, idx).Trim().ToString();
            var value = p.AsSpan(idx + 1).Trim().ToString();
            // Trim optional quotes
            if (value is ['"', _, ..] && value[^1] == '"')
            {
                value = value.Substring(1, value.Length - 2);
            }

            switch (key.ToLowerInvariant())
            {
                case "v1":
                    if (seen.Add(value)) result.V1Base64.Add(value);
                    break;
                case "t":
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
                    {
                        result.TimestampUnixSeconds = ts;
                    }

                    break;
                case "k":
                case "kid":
                    result.KeyId = value;
                    break;
                // ignore unknown keys deliberately
            }
        }

        // Consider parse successful if we recognized at least one known field.
        return result.TimestampUnixSeconds.HasValue || result.V1Base64.Count > 0 || result.KeyId is not null;
    }

// Accept base64url (preferred, unpadded) or standard base64; normalize and decode.
    private static bool TryDecodeBase64OrBase64Url(string value, out byte[] bytes)
    {
        // Heuristic: base64url if it contains '-' or '_' or lacks '+' and '/'. Normalize to base64.
        bool looksUrl = value.IndexOf('-') >= 0 || value.IndexOf('_') >= 0 ||
                        (value.IndexOf('+') < 0 && value.IndexOf('/') < 0);
        string normalized;
        if (looksUrl)
        {
            normalized = value.Replace('-', '+').Replace('_', '/');
            int padding = normalized.Length % 4;
            if (padding != 0)
            {
                normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
            }
        }
        else
        {
            normalized = value;
        }

        try
        {
            bytes = Convert.FromBase64String(normalized);
            return true;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }

    private sealed class SignatureHeaderComponents
    {
        public List<string> V1Base64 { get; } = new(capacity: 2);
        public long? TimestampUnixSeconds { get; set; }
        public string? KeyId { get; set; }
    }

    private static async Task<byte[]?> ReadBodyWithLimitAsync(HttpRequest request, int maxBytes, CancellationToken ct)
    {
        // Fast-path: empty body
        if (request.ContentLength is 0)
        {
            return [];
        }

        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int total = 0;
            using var ms = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int read = await request.Body.ReadAsync(rented.AsMemory(0, Math.Min(rented.Length, maxBytes - total)),
                    ct);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    return null; // too large
                }

                ms.Write(rented, 0, read);
            }

            return ms.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }
}
