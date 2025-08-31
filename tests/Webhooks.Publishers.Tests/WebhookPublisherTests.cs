using System.Net;
using Microsoft.AspNetCore.TestHost;
using Webhooks.Receivers;
using Webhooks.Tests.Common;

namespace Webhooks.Publishers.Tests;

public class WebhookPublisherTests
{
    private static readonly byte[] DefaultKey = "publishersecretkey000000000000000"u8.ToArray();

    [Fact]
    public async Task Publisher_Headers_Validate_In_Middleware()
    {
        const long t = 1700000000;
        var server = CreateServer(t);
        var httpClient = server.CreateClient();
        var publisher = new WebhookPublisher(httpClient,
            new StaticTimeProvider(t), new FixedValidationWebhookKeyRetriever(DefaultKey));

        var payload = "{\"n\":42}"u8.ToArray();
        var msgId = "evt_pub_1";
        var response = await publisher.PublishAsync(httpClient.BaseAddress!, msgId, payload);

        Assert.Equal(StatusCodes.Status204NoContent, (int)response.StatusCode);
    }

    private static TestServer CreateServer(long timestamp)
    {
        var builder = new WebHostBuilder()
            .Configure(app =>
            {
                app.UseMiddleware<SymmetricKeyWebhookValidationMiddleware>();
                app.Run(context =>
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                    return Task.CompletedTask;
                });
            })
            .ConfigureServices(services =>
            {
                // Dependencies required by the filter
                services.AddSingleton<TimeProvider>(new StaticTimeProvider(timestamp));
                services.AddSingleton<IValidationWebhookKeyRetriever>(
                    new FixedValidationWebhookKeyRetriever(DefaultKey));
            });

        return new TestServer(builder);
    }
}

internal sealed class FixedValidationWebhookKeyRetriever(byte[] key)
    : IValidationWebhookKeyRetriever, IPublisherKeyRetriever
{
    public byte[] GetKey(HttpContext context) => key;
    public byte[] GetKey() => key;
}
