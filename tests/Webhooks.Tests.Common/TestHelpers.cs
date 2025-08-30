using Microsoft.AspNetCore.Http;

namespace Webhooks.Tests.Common;

public static class TestHelpers
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
}

public sealed class StaticTimeProvider(long unixSeconds) : TimeProvider
{
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    public override DateTimeOffset GetUtcNow() => _now;
}