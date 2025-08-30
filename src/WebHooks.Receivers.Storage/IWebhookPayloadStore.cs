namespace WebHooks.Receivers.Storage;

public interface IWebhookPayloadStore
{
    Task StorePayloadAsync(
        string id,
        DateTimeOffset receivedAt,
        string payload,
        CancellationToken cancellationToken = default);
}