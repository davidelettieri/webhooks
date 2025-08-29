using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace StandardWebhooks;

/// <summary>
/// Publishes webhooks that are verifiable by <see cref="SymmetricKeyWebhookValidationFilter"/>.
/// </summary>
public sealed class WebhookPublisher(HttpClient httpClient, TimeProvider timeProvider, IKeyRetriever keyRetriever)
{
    private static readonly UTF8Encoding SafeUtf8Encoding =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    private readonly IKeyRetriever
        _keyRetriever = keyRetriever ?? throw new ArgumentNullException(nameof(keyRetriever));

    private readonly EndpointFilterInvocationContext _keyContext = new PublisherEndpointFilterInvocationContext();

    /// <summary>
    /// Builds the value for the webhook-signature header for the given inputs using base64url (unpadded).
    /// </summary>
    private string BuildSignatureHeader(string messageId, DateTimeOffset timestamp, ReadOnlySpan<byte> payload)
    {
        if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentException("messageId required", nameof(messageId));
        if (messageId.Length > 256) throw new ArgumentException("messageId too long (max 256)", nameof(messageId));

        var key = _keyRetriever.GetKey(_keyContext);
        var tag = ComputeTag(key, messageId, timestamp, payload);
        try
        {
            var sig = Base64UrlEncode(tag);
            return $"t={timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}, v1={sig}";
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    /// <summary>
    /// Creates an HttpRequestMessage with proper headers and body.
    /// </summary>
    public HttpRequestMessage CreateRequest(Uri endpoint, string messageId, ReadOnlyMemory<byte> payload,
        string contentType = "application/json")
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        var now = timeProvider.GetUtcNow();
        var sigHeader = BuildSignatureHeader(messageId, now, payload.Span);
        var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(payload.ToArray())
        };
        req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        req.Headers.TryAddWithoutValidation("webhook-id", messageId);
        req.Headers.TryAddWithoutValidation("webhook-signature", sigHeader);
        return req;
    }

    /// <summary>
    /// Sends a webhook POST to the endpoint with the provided body.
    /// </summary>
    public Task<HttpResponseMessage> PublishAsync(Uri endpoint, string messageId, ReadOnlyMemory<byte> payload,
        string contentType = "application/json", CancellationToken cancellationToken = default)
    {
        var request = CreateRequest(endpoint, messageId, payload, contentType);
        return _httpClient.SendAsync(request, cancellationToken);
    }

    private static byte[] ComputeTag(byte[] key, string msgId, DateTimeOffset timestamp,
        ReadOnlySpan<byte> payload)
    {
        // Build prefix "msgId.timestamp." as UTF-8 and hash with the payload.
        var prefix = $"{msgId}.{timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}.";
        byte[]? rented = null;
        byte[] prefixBytes;
        int count = SafeUtf8Encoding.GetByteCount(prefix);
        if (count <= 512)
        {
            prefixBytes = System.Buffers.ArrayPool<byte>.Shared.Rent(count);
            rented = prefixBytes;
            count = SafeUtf8Encoding.GetBytes(prefix, 0, prefix.Length, prefixBytes, 0);
        }
        else
        {
            prefixBytes = SafeUtf8Encoding.GetBytes(prefix);
        }

        try
        {
            using var ih = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
            ih.AppendData(prefixBytes, 0, count);
            ih.AppendData(payload);
            return ih.GetHashAndReset();
        }
        finally
        {
            if (rented is not null)
            {
                CryptographicOperations.ZeroMemory(rented.AsSpan(0, count));
                System.Buffers.ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            }
            else
            {
                CryptographicOperations.ZeroMemory(prefixBytes);
            }
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var s = Convert.ToBase64String(bytes);
        return s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Minimal invocation context used solely for key retrieval via IKeyRetriever in publisher scenarios.
    private sealed class PublisherEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        private static readonly HttpContext s_httpContext = new DefaultHttpContext();
        private static readonly IList<object?> s_arguments = Array.Empty<object?>();

        public override T GetArgument<T>(int index) => throw new NotSupportedException();
        public override HttpContext HttpContext => s_httpContext;
        public override IList<object?> Arguments => s_arguments;
    }
}