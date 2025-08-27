using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace StandardWebhooks;

public sealed class SimmetricKeyWebhookValidationFilter(
    ILogger<SimmetricKeyWebhookValidationFilter> logger,
    TimeProvider timeProvider,
    IKeyRetriever keyRetriever)
    : IEndpointFilter
{
    private readonly ILogger<SimmetricKeyWebhookValidationFilter> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IKeyRetriever _keyRetriever = keyRetriever;

    // Keep a strict, safe encoding instance if ever needed for text parts
    private static readonly UTF8Encoding SafeUtf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Header names
    private const string IdHeaderKey = "webhook-id";
    private const string SignatureHeaderKey = "webhook-signature";
    private const string TimestampHeaderKey = "webhook-timestamp";

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

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var request = httpContext.Request;
        var ct = httpContext.RequestAborted;

        // Extract and validate required headers first (cheapest checks).
        var headers = request.Headers;

        string? msgId = headers.TryGetValue(IdHeaderKey, out var unbrandedId) ? unbrandedId.ToString() : null;
        string? msgSignature = headers.TryGetValue(SignatureHeaderKey, out var signatureHeader) ? signatureHeader.ToString() : null;
        string? msgTimestamp = headers.TryGetValue(TimestampHeaderKey, out var timestampHeader) ? timestampHeader.ToString() : null;

        if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(msgSignature) || string.IsNullOrWhiteSpace(msgTimestamp))
        {
            _logger.LogWarning("Webhook rejected: missing required headers.");
            return Results.Unauthorized();
        }

        if (msgId.Length > MaxMessageIdChars)
        {
            _logger.LogWarning("Webhook rejected: message id too large.");
            return Results.Unauthorized();
        }

        if (!VerifyTimestamp(msgTimestamp, out var timestamp))
        {
            _logger.LogWarning("Webhook rejected: invalid or out-of-tolerance timestamp.");
            return Results.Unauthorized();
        }

        // Get signing key early; don't spend cycles if we cannot verify.
        var key = _keyRetriever.GetKey(context);
        if (key is null || key.Length == 0)
        {
            _logger.LogWarning("Webhook rejected: no signing key available.");
            return Results.Unauthorized();
        }

        // Short-circuit on known-too-large bodies
        if (request.ContentLength is long contentLength && contentLength > MaxPayloadBytes)
        {
            _logger.LogWarning("Webhook rejected: payload too large. Content-Length={Length}", contentLength);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
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
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (IOException)
        {
            // Likely due to max buffer exceeded in server buffering layer.
            _logger.LogWarning("Webhook rejected: payload exceeded buffering limits.");
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch
        {
            _logger.LogWarning("Webhook rejected: failed to read request body.");
            return Results.Unauthorized();
        }

        if (payloadBytes is null)
        {
            _logger.LogWarning("Webhook rejected: payload missing or too large.");
            return Results.Unauthorized();
        }

        // Rewind body for downstream consumers.
        request.Body.Position = 0;

        // Compute expected signature over raw bytes without concatenating large strings.
        var expectedSignature = Sign(key, msgId, timestamp.Value, payloadBytes);

        // Process at most a small number of tokens to avoid header inflation DoS.
        var tokens = msgSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > MaxSignatureTokens)
        {
            _logger.LogWarning("Webhook rejected: too many signature entries.");
            CryptographicOperations.ZeroMemory(expectedSignature);
            Array.Clear(payloadBytes, 0, payloadBytes.Length);
            return Results.Unauthorized();
        }

        foreach (var token in tokens)
        {
            // Expect "v1,<base64>"
            var parts = token.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue; // ignore malformed
            }

            var version = parts[0];
            if (!string.Equals(version, "v1", StringComparison.Ordinal))
            {
                continue; // ignore unknown versions
            }

            var b64 = parts[1];
            if (b64.Length > MaxBase64SignatureChars)
            {
                continue; // avoid expensive decodes
            }

            byte[]? providedSigBytes;
            try
            {
                providedSigBytes = Convert.FromBase64String(b64);
            }
            catch (FormatException)
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
                return await next(context);
            }
        }

        _logger.LogWarning("Webhook rejected: signature validation failed.");
        CryptographicOperations.ZeroMemory(expectedSignature);
        Array.Clear(payloadBytes, 0, payloadBytes.Length);
        return Results.Unauthorized();
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
        // Build prefix "msgId.timestamp." as UTF-8, then stream payload bytes into the HMAC.
        var prefix = $"{msgId}.{timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}.";
        var prefixBytes = SafeUtf8Encoding.GetBytes(prefix);

        using var hmac = new HMACSHA256(key);
        // Stream into HMAC to avoid concatenating large arrays.
        hmac.TransformBlock(prefixBytes, 0, prefixBytes.Length, null, 0);
        hmac.TransformFinalBlock(payloadBytes.ToArray(), 0, payloadBytes.Length);
        // hmac.Hash is the computed tag
        return hmac.Hash!;
    }

    private static async Task<byte[]?> ReadBodyWithLimitAsync(HttpRequest request, int maxBytes, CancellationToken ct)
    {
        // Fast-path: empty body
        if (request.ContentLength is long len && len == 0)
        {
            return Array.Empty<byte>();
        }

        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int total = 0;
            using var ms = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int read = await request.Body.ReadAsync(rented.AsMemory(0, Math.Min(rented.Length, maxBytes - total)), ct);
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

public interface IKeyRetriever
{
    byte[] GetKey(EndpointFilterInvocationContext context);
}

public sealed class WebhookValidationFilterOptions
{
    /// <summary>
    /// Symmetric secret used to verify webhook signatures (implementation-specific).
    /// </summary>
    public string Key { get; set; } = string.Empty;
}