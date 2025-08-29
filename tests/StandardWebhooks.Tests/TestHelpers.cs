using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

    public static EndpointFilterInvocationContext CreateInvocationContext(HttpContext httpContext)
    {
        // Minimal context with route data, not used by filter logic
        var routeData = new RouteData();
        return new DefaultEndpointFilterInvocationContext(httpContext, routeData, new List<object?>());
    }

    public static ILogger<SymmetricKeyWebhookValidationFilter> NullLogger() =>
        new NullLogger<SymmetricKeyWebhookValidationFilter>();
}

internal sealed class StaticTimeProvider(long unixSeconds) : TimeProvider
{
    private readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
    public override DateTimeOffset GetUtcNow() => _now;
}

internal sealed class FixedKeyRetriever(byte[] key) : IKeyRetriever
{
    private readonly byte[] _key = key;
    public byte[] GetKey(EndpointFilterInvocationContext context) => _key;
}

internal sealed class DefaultEndpointFilterInvocationContext(
    HttpContext httpContext,
    RouteData _,
    IReadOnlyList<object?> arguments)
    : EndpointFilterInvocationContext
{
    public object? FindArgument(Type type) => arguments.FirstOrDefault(a => a?.GetType() == type);

    public override T GetArgument<T>(int index)
    {
        throw new NotImplementedException();
    }

    public override HttpContext HttpContext => httpContext;
    public override IList<object?> Arguments => [..arguments];
}