using System.Diagnostics.CodeAnalysis;
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
    private static readonly UTF8Encoding SafeUtf8Encoding = new UTF8Encoding(false, true);
    private const string UnbrandedIdHeaderKey = "webhook-id";
    private const string UnbrandedSignatureHeaderKey = "webhook-signature";
    private const string UnbrandedTimestampHeaderKey = "webhook-timestamp";

    private const int ToleranceInSeconds = 60 * 5;
    private static string _keyPrefix = "whsec_";
    private readonly byte[] _signingKey;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var headers = context.HttpContext.Request.Headers;
        string? msgId =
            headers.TryGetValue(UnbrandedIdHeaderKey, out var unbrandedId) ? unbrandedId.ToString() : null;
        string? msgSignature =
            headers.TryGetValue(UnbrandedSignatureHeaderKey, out var value) ? value.ToString() : null;
        string? msgTimestamp =
            headers.TryGetValue(UnbrandedTimestampHeaderKey, out var value2) ? value2.ToString() : null;

        if (String.IsNullOrEmpty(msgId) || String.IsNullOrEmpty(msgSignature) || String.IsNullOrEmpty(msgTimestamp))
        {
            return Results.Unauthorized();
        }

        if (!VerifyTimestamp(msgTimestamp, out var timestamp))
        {
            return Results.Unauthorized();
        }

        using var reader = new StreamReader(context.HttpContext.Request.Body, SafeUtf8Encoding);
        var payload = await reader.ReadToEndAsync();
        var key = _keyRetriever.GetKey(context);
        var signature = Sign(key, msgId, timestamp.Value, payload);
        var passedSignatures = msgSignature.Split(' ');
        
        foreach (string versionedSignature in passedSignatures)
        {
            var parts = versionedSignature.Split(',');
            if (parts.Length < 2)
            {
                return Results.Unauthorized();
            }

            var version = parts[0];
            var passedSignature = parts[1];

            if (version != "v1")
            {
                continue;
            }
            ReadOnlySpan<byte> verifyBytes = Convert.FromBase64String(passedSignature);
            if (CryptographicOperations.FixedTimeEquals(signature, verifyBytes))
            {
                return await next(context);
            }
        }

        return Results.Unauthorized();
    }

    private bool VerifyTimestamp(string timestampHeader, [NotNullWhen(true)] out DateTimeOffset? timestamp)
    {
        var now = _timeProvider.GetUtcNow();
        try
        {
            var timestampInt = long.Parse(timestampHeader);
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

    private byte[] Sign(byte[] key, string msgId, DateTimeOffset timestamp, string payload)
    {
        var toSign = $"{msgId}.{timestamp.ToUnixTimeSeconds().ToString()}.{payload}";
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
    /// 
    /// </summary>
    public string Key { get; set; }
}