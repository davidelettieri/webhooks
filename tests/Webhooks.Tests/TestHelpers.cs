using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Webhooks.Publishers;
using Webhooks.Receivers;

namespace StandardWebhooks.Tests;

internal static class TestHelpers
{
    public static DefaultHttpContext CreateHttpContext(string method = "POST", string path = "/", byte[]? body = null)
    {
        var ctx = new DefaultHttpContext
        {
            Request =
            {
                Method = method,
                Path = path
            }
        };
        if (body is not null)
        {
            ctx.Request.Body = new MemoryStream(body, writable: false);
            ctx.Request.ContentLength = body.Length;
        }

        return ctx;
    }

    public static ILogger<SymmetricKeyWebhookValidationMiddleware> NullLogger() =>
        new NullLogger<SymmetricKeyWebhookValidationMiddleware>();
}

internal sealed class StaticTimeProvider(long unixSeconds) : TimeProvider
{
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    public override DateTimeOffset GetUtcNow() => _now;
}

internal sealed class FixedValidationWebhookKeyRetriever(byte[] key)
    : IValidationWebhookKeyRetriever, IPublisherKeyRetriever
{
    private readonly byte[] _key = key;
    public byte[] GetKey(HttpContext context) => _key;
    public byte[] GetKey() => _key;
}

 