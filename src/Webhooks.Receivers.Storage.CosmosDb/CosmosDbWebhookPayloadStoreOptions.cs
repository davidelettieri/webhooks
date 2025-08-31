namespace Webhooks.Receivers.Storage.CosmosDb;

public sealed class CosmosDbWebhookPayloadStoreOptions
{
    public required string Database { get; init; } = "webhooks";
    public required string Container { get; init; } = "payloads";
}
