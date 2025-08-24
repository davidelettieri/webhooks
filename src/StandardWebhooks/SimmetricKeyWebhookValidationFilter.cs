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
    private static readonly UTF8Encoding SafeUtf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private const string UnbrandedIdHeaderKey = "webhook-id";
    private const string UnbrandedSignatureHeaderKey = "webhook-signature";
    private const string UnbrandedTimestampHeaderKey = "webhook-timestamp";
    private const int ToleranceInSeconds = 60 * 5;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var headers = context.HttpContext.Request.Headers;

        string? msgId = headers.TryGetValue(UnbrandedIdHeaderKey, out var unbrandedId) ? unbrandedId.ToString() : null;
        string? msgSignature = headers.TryGetValue(UnbrandedSignatureHeaderKey, out var signatureHeader) ? signatureHeader.ToString() : null;
        string? msgTimestamp = headers.TryGetValue(UnbrandedTimestampHeaderKey, out var timestampHeader) ? timestampHeader.ToString() : null;

        if (string.IsNullOrWhiteSpace(msgId) || string.IsNullOrWhiteSpace(msgSignature) || string.IsNullOrWhiteSpace(msgTimestamp))
        {
            _logger.LogWarning("Webhook rejected: missing required headers. HasId={HasId}, HasSignature={HasSig}, HasTimestamp={HasTs}",
                !string.IsNullOrWhiteSpace(msgId), !string.IsNullOrWhiteSpace(msgSignature), !string.IsNullOrWhiteSpace(msgTimestamp));
            return Results.Unauthorized();
        }

        if (!VerifyTimestamp(msgTimestamp, out var timestamp))
        {
            _logger.LogWarning("Webhook rejected: invalid or out-of-tolerance timestamp header value '{TimestampHeader}'.", msgTimestamp);
            return Results.Unauthorized();
        }

        // Get signing key
        var key = _keyRetriever.GetKey(context);
        if (key is null || key.Length == 0)
        {
            _logger.LogWarning("Webhook rejected: no signing key available for the request context.");
            return Results.Unauthorized();
        }

        // Read request body safely without preventing downstream from reading it again.
        var request = context.HttpContext.Request;
        request.EnableBuffering(); // ensure the body can be read multiple times
        request.Body.Position = 0;

        string payload;
        using (var reader = new StreamReader(request.Body, SafeUtf8Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(context.HttpContext.RequestAborted);
        }
        request.Body.Position = 0; // rewind for downstream

        // Compute expected signature
        var expectedSignature = Sign(key, msgId, timestamp.Value, payload);

        // Support multiple signatures in the header, accept if any matches.
        var versionedTokens = msgSignature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in versionedTokens)
        {
            // Expect "v1,<base64>"
            var parts = token.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                // Ignore malformed token rather than failing the whole request.
                continue;
            }

            var version = parts[0];
            if (!string.Equals(version, "v1", StringComparison.Ordinal))
            {
                continue; // ignore unknown versions
            }

            byte[] providedSigBytes;
            try
            {
                providedSigBytes = Convert.FromBase64String(parts[1]);
            }
            catch (FormatException)
            {
                // Ignore invalid base64 entries
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(expectedSignature, providedSigBytes))
            {
                return await next(context);
            }
        }

        _logger.LogWarning("Webhook rejected: no valid signature matched for message id '{MessageId}'.", msgId);
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

    private static byte[] Sign(byte[] key, string msgId, DateTimeOffset timestamp, string payload)
    {
        var toSign = $"{msgId}.{timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}.{payload}";
        var toSignBytes = SafeUtf8Encoding.GetBytes(toSign);
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(toSignBytes);
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