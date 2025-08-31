using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using WebHooks.Receivers.Storage;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Webhooks.Receivers.Storage.CosmosDb;

public sealed class CosmosDbSimpleWebhookPayloadStore(
    IOptions<CosmosDbWebhookPayloadStoreOptions> options,
    CosmosClient client) : ISimpleWebhookPayloadStore
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
            ReceivedAt = receivedAt,
            Type = null
        };

        await _container.CreateItemAsync(document, new PartitionKey(document.Id),
            cancellationToken: cancellationToken);
    }
}
