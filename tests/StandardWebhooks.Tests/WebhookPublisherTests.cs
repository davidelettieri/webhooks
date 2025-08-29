using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;
using StandardWebhooks;

namespace StandardWebhooks.Tests;

public class WebhookPublisherTests
{
    [Fact]
    public async Task Publisher_Headers_Validate_In_Filter()
    {
        var key = "publishersecretkey000000000000000"u8.ToArray();
        var publisher = new WebhookPublisher(new HttpClient(new SocketsHttpHandler()), new StaticTimeProvider(1_700_000_000), key, keyId: "kid1");

        var payload = "{\"n\":42}"u8.ToArray();
        var msgId = "evt_pub_1";
        var req = publisher.CreateRequest(new Uri("https://example.test/hook"), msgId, payload);

        var ctx = TestHelpers.CreateHttpContext(body: payload);
        ctx.Request.Headers["webhook-id"] = req.Headers.GetValues("webhook-id").First();
        ctx.Request.Headers["webhook-signature"] = req.Headers.GetValues("webhook-signature").First();

    var filter = new SymmetricKeyWebhookValidationFilter(TestHelpers.NullLogger(), new StaticTimeProvider(1_700_000_000), new FixedKeyRetriever(key));
        var inv = TestHelpers.CreateInvocationContext(ctx);
        var res = await filter.InvokeAsync(inv, _ => ValueTask.FromResult<object?>(Results.Ok()));
        Assert.IsType<Ok>(res);
    }
}
