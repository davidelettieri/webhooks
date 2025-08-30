using Microsoft.AspNetCore.Http;
using Xunit;
using Webhooks.Publishers;
using Webhooks.Receivers;

namespace StandardWebhooks.Tests;

public class WebhookPublisherTests
{
    [Fact]
    public async Task Publisher_Headers_Validate_In_Middleware()
    {
        var key = "publishersecretkey000000000000000"u8.ToArray();
        var publisher = new WebhookPublisher(new HttpClient(new SocketsHttpHandler()),
            new StaticTimeProvider(1_700_000_000), new FixedValidationWebhookKeyRetriever(key));

        var payload = "{\"n\":42}"u8.ToArray();
        var msgId = "evt_pub_1";
        var req = publisher.CreateRequest(new Uri("https://example.test/hook"), msgId, payload);

        var ctx = TestHelpers.CreateHttpContext(body: payload);
        ctx.Request.Headers["webhook-id"] = req.Headers.GetValues("webhook-id").First();
        ctx.Request.Headers["webhook-signature"] = req.Headers.GetValues("webhook-signature").First();

        var mw = new SymmetricKeyWebhookValidationMiddleware(TestHelpers.NullLogger(),
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