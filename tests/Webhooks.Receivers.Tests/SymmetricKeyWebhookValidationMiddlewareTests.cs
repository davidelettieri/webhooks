using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Webhooks.Tests.Common;

namespace Webhooks.Receivers.Tests;

public class SymmetricKeyWebhookValidationMiddlewareTests
{
    private static readonly ILogger<SymmetricKeyWebhookValidationMiddleware> Logger =
        new NullLogger<SymmetricKeyWebhookValidationMiddleware>();

    private static byte[] Sign(byte[] key, string id, long t, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{id}.{t.ToString(CultureInfo.InvariantCulture)}.");
        using var hmac = new HMACSHA256(key);
        var all = new byte[prefix.Length + body.Length];
        Buffer.BlockCopy(prefix, 0, all, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, all, prefix.Length, body.Length);
        return hmac.ComputeHash(all);
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes);

    private static string B64Url(byte[] bytes)
    {
        var s = Convert.ToBase64String(bytes).TrimEnd('=');
        return s.Replace('+', '-').Replace('/', '_');
    }

    private static RequestDelegate NextPass => ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        return Task.CompletedTask;
    };

    [Fact]
    public async Task Accepts_Valid_Base64Url_Signature()
    {
        var key = Encoding.UTF8.GetBytes("supersecretkey000000000000000000");
        var id = "evt_123";
        var t = 1_700_000_000L;
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var tag = Sign(key, id, t, body);
        var sig = B64Url(tag);

        var ctx = TestHelpers.CreateHttpContext(body: body);
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-signature"] = $"v1={sig}";
        ctx.Request.Headers["webhook-timestamp"] = t.ToString(CultureInfo.InvariantCulture);

        var mw = new SymmetricKeyWebhookValidationMiddleware(
            Logger,
            new StaticTimeProvider(t),
            new FixedValidationWebhookKeyRetriever(key), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Accepts_Valid_Standard_Base64_Signature()
    {
        var key = Encoding.UTF8.GetBytes("anothersecretkey000000000000000");
        var id = "evt_456";
        var t = 1_700_000_100L;
        var body = Encoding.UTF8.GetBytes("hello");
        var tag = Sign(key, id, t, body);
        var sig = B64(tag);

        var ctx = TestHelpers.CreateHttpContext(body: body);
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-signature"] = $"v1={sig}";
        ctx.Request.Headers["webhook-timestamp"] = t.ToString(CultureInfo.InvariantCulture);

        var mw = new SymmetricKeyWebhookValidationMiddleware(
            Logger,
            new StaticTimeProvider(t),
            new FixedValidationWebhookKeyRetriever(key), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Rejects_Missing_Headers()
    {
        var t = 1_700_000_000L;
        var ctx = TestHelpers.CreateHttpContext(body: Encoding.UTF8.GetBytes("{}"));
        // Missing id and signature
        var mw = new SymmetricKeyWebhookValidationMiddleware(
            new NullLogger<SymmetricKeyWebhookValidationMiddleware>(),
            new StaticTimeProvider(t),
            new FixedValidationWebhookKeyRetriever(Encoding.UTF8.GetBytes("k")), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Rejects_Missing_v1_or_t()
    {
        var key = Encoding.UTF8.GetBytes("k".PadRight(32, 'k'));
        var t = 1_700_000_000L;
        var ctx = TestHelpers.CreateHttpContext(body: Encoding.UTF8.GetBytes("{}"));
        ctx.Request.Headers["webhook-id"] = "evt";
        ctx.Request.Headers["webhook-signature"] = "v1=abc"; // missing t
        var mw = new SymmetricKeyWebhookValidationMiddleware(
            Logger,
            new StaticTimeProvider(t),
            new FixedValidationWebhookKeyRetriever(key), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);

        ctx = TestHelpers.CreateHttpContext(body: Encoding.UTF8.GetBytes("{}"));
        ctx.Request.Headers["webhook-id"] = "evt";
        ctx.Request.Headers["webhook-signature"] = ""; // missing v1
        ctx.Request.Headers["webhook-timestamp"] = t.ToString(CultureInfo.InvariantCulture);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Respects_Timestamp_Tolerance()
    {
        var key = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");
        var id = "evt";
        var body = Encoding.UTF8.GetBytes("{}");
        var now = 1_700_000_000L;
        var within = now + 299; // inside 5 minutes
        var tag = Sign(key, id, within, body);
        var ctx = TestHelpers.CreateHttpContext(body: body);
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-signature"] = $"v1={B64Url(tag)}";
        ctx.Request.Headers["webhook-timestamp"] = within.ToString(CultureInfo.InvariantCulture);
        var mw = new SymmetricKeyWebhookValidationMiddleware(
            Logger,
            new StaticTimeProvider(now),
            new FixedValidationWebhookKeyRetriever(key), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);

        var outside = now + 301; // just outside
        tag = Sign(key, id, outside, body);
        ctx = TestHelpers.CreateHttpContext(body: body);
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-signature"] = $"v1={B64Url(tag)}";
        ctx.Request.Headers["webhook-timestamp"] = outside.ToString(CultureInfo.InvariantCulture);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Enforces_Signature_Token_Cap()
    {
        var key = Encoding.UTF8.GetBytes("x".PadRight(32, 'x'));
        var id = "evt";
        var t = 1_700_000_000L;
        var body = Encoding.UTF8.GetBytes("{}");
        var tag = B64Url(Sign(key, id, t, body));
        // Create more than cap
        var sig = string.Join(", ", Enumerable.Range(0, 11).Select(_ => "v1=" + tag));
        var ctx = TestHelpers.CreateHttpContext(body: body);
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-signature"] = sig;
        ctx.Request.Headers["webhook-timestamp"] = t.ToString(CultureInfo.InvariantCulture);
        var mw = new SymmetricKeyWebhookValidationMiddleware(
            Logger,
            new StaticTimeProvider(t),
            new FixedValidationWebhookKeyRetriever(key), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Rejects_Wrong_Signature()
    {
        var key = Encoding.UTF8.GetBytes("k".PadRight(32, 'k'));
        var id = "evt";
        var t = 1_700_000_000L;
        var body = Encoding.UTF8.GetBytes("{}");
        var wrong = Convert.ToBase64String(Encoding.UTF8.GetBytes("notasig"));
        var ctx = TestHelpers.CreateHttpContext(body: body);
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-signature"] = $"v1={wrong}";
        ctx.Request.Headers["webhook-timestamp"] = t.ToString(CultureInfo.InvariantCulture);
        var mw = new SymmetricKeyWebhookValidationMiddleware(
            Logger,
            new StaticTimeProvider(t),
            new FixedValidationWebhookKeyRetriever(key), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Rejects_Oversized_Body()
    {
        var key = Encoding.UTF8.GetBytes("k".PadRight(32, 'k'));
        var id = "evt";
        var t = 1_700_000_000L;
        var body = new byte[256 * 1024 + 1];
        var tag = B64Url(Sign(key, id, t, [])); // body won't match anyway
        var ctx = TestHelpers.CreateHttpContext(body: body);
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-signature"] = $"v1={tag}";
        ctx.Request.Headers["webhook-timestamp"] = t.ToString(CultureInfo.InvariantCulture);
        var mw = new SymmetricKeyWebhookValidationMiddleware(
            Logger,
            new StaticTimeProvider(t),
            new FixedValidationWebhookKeyRetriever(key), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Cancellation_Yields_499_Status()
    {
        var key = Encoding.UTF8.GetBytes("k".PadRight(32, 'k'));
        var id = "evt";
        var t = 1_700_000_000L;
        var body = Encoding.UTF8.GetBytes(new string('a', 10_000));
        var ctx = TestHelpers.CreateHttpContext(body: body);
        var cts = new CancellationTokenSource();
        ctx.RequestAborted = cts.Token;
        ctx.Request.Headers["webhook-id"] = id;
        ctx.Request.Headers["webhook-signature"] = $"v1={B64Url(Sign(key, id, t, body))}";
        ctx.Request.Headers["webhook-timestamp"] = t.ToString(CultureInfo.InvariantCulture);

        // Cancel before read
        cts.Cancel();
        var mw = new SymmetricKeyWebhookValidationMiddleware(new NullLogger<SymmetricKeyWebhookValidationMiddleware>(),
            new StaticTimeProvider(t),
            new FixedValidationWebhookKeyRetriever(key), NextPass);
        await mw.InvokeAsync(ctx);
        Assert.Equal(499, ctx.Response.StatusCode);
    }
}
