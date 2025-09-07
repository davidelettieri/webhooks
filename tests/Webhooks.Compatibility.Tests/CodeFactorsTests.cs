using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Http;
using StandardWebhooks;
using Webhooks.Publishers;
using Webhooks.Tests.Common;

namespace Webhooks.Compatibility.Tests;

public sealed class CodeFactorsTests
{
    [Fact]
    public async Task Test1()
    {
        const long timestamp = 1_700_000_000L;
        const string secret = "publishersecretkey000000000000000";
        const string testPayload = "test payload";
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var base64Key = Convert.ToBase64String(keyBytes);

        var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://unit.test/")
        };

        var publisher = new WebhookPublisher(
            httpClient,
            new StaticTimeProvider(timestamp),
            new FixedPublisherKeyRetriever(keyBytes));

        var payload = Encoding.UTF8.GetBytes(testPayload);
        var msgId = "evt_compat_1";

        var response = await publisher.PublishAsync(httpClient.BaseAddress!, msgId, payload);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(handler.CapturedRequest);
        Assert.NotNull(handler.CapturedBody);

        var webhook = new StandardWebhook(base64Key);
        var expectedSigHeader = webhook.Sign(msgId, DateTimeOffset.FromUnixTimeSeconds(timestamp), testPayload);
        var receivedSigHeader = handler.CapturedRequest!.Headers.GetValues("webhook-signature").Single();

        Assert.Equal(expectedSigHeader, receivedSigHeader);
    }

    private sealed class FixedPublisherKeyRetriever(byte[] key) : IPublisherKeyRetriever
    {
        public byte[] GetKey() => key;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            CapturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
