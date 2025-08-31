namespace WebHooks.Receivers.Storage;

public interface IWebhookPayloadStore
{
    Task StorePayloadAsync<TPayload>(
        string id,
        DateTimeOffset receivedAt,
        TPayload payload,
        CancellationToken cancellationToken = default);
}
