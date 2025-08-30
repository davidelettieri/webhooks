using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Webhooks.Receivers;
using Webhooks.Tests.Common;

namespace Webhooks.Publishers.Tests;

public class WebhookPublisherTests
{
    [Fact]
    public async Task Publisher_Headers_Validate_In_Middleware()
    {
        const long t = 1700000000;
        var key = "publishersecretkey000000000000000"u8.ToArray();
        var publisher = new WebhookPublisher(new HttpClient(new SocketsHttpHandler()),
            new StaticTimeProvider(t), new FixedValidationWebhookKeyRetriever(key));

        var payload = "{\"n\":42}"u8.ToArray();
        var msgId = "evt_pub_1";
        var req = publisher.CreateRequest(new Uri("https://example.test/hook"), msgId, payload);

        var ctx = TestHelpers.CreateHttpContext(body: payload);
        ctx.Request.Headers["webhook-id"] = req.Headers.GetValues("webhook-id").First();
        ctx.Request.Headers["webhook-signature"] = req.Headers.GetValues("webhook-signature").First();
        ctx.Request.Headers["webhook-timestamp"] = req.Headers.GetValues("webhook-timestamp").First();
        var mw = new SymmetricKeyWebhookValidationMiddleware(new NullLogger<SymmetricKeyWebhookValidationMiddleware>(),
            new StaticTimeProvider(1_700_000_000), new FixedValidationWebhookKeyRetriever(key),
            _ =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            });
        await mw.InvokeAsync(ctx);
        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
    }
}

internal sealed class FixedValidationWebhookKeyRetriever(byte[] key)
    : IValidationWebhookKeyRetriever, IPublisherKeyRetriever
{
    public byte[] GetKey(HttpContext context) => key;
    public byte[] GetKey() => key;
}