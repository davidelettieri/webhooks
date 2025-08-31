using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using WebHooks.Receivers.Storage;

namespace Webhooks.Receivers.Storage.CosmosDb;

public sealed class CosmosDbWebhookPayloadStore(
    IOptions<CosmosDbWebhookPayloadStoreOptions> options,
    CosmosClient client) : IWebhookPayloadStore
{
    private readonly Container _container = client.GetContainer(options.Value.Database, options.Value.Container);

    public async Task StorePayloadAsync(
        string id,
        DateTimeOffset receivedAt,
        string payload,
        CancellationToken cancellationToken = default)
    {
        var document = new WebhookPayloadDocument
        {
            Id = id,
            Payload = payload,
            ReceivedAt = receivedAt
        };

        await _container.CreateItemAsync(document, new PartitionKey(document.Id),
            cancellationToken: cancellationToken);
    }

    private sealed class WebhookPayloadDocument
    {
        [JsonProperty("id")] public string Id { get; init; } = null!;
        public string Payload { get; init; } = null!;
        public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    }
}
