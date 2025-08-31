namespace WebHooks.Receivers.Storage;

public interface ISimpleWebhookPayloadStore
{
    Task StorePayloadAsync(
        string id,
        DateTimeOffset receivedAt,
        string payload,
        CancellationToken cancellationToken = default);
}
