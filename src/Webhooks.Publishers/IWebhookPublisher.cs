namespace Webhooks.Publishers;

public interface IWebhookPublisher
{
    /// <summary>
    /// Sends a webhook POST to the endpoint with the provided body.
    /// </summary>
    Task<HttpResponseMessage> PublishAsync(Uri endpoint, string messageId, ReadOnlyMemory<byte> payload,
        string contentType = "application/json", CancellationToken cancellationToken = default);
}
