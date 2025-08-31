using Newtonsoft.Json;
using System.Text.Json;

namespace Webhooks.Receivers.Storage.CosmosDb;

sealed class WebhookPayloadDocument
{
    [JsonProperty("id")]
    public string Id { get; init; } = null!;
    public string Payload { get; init; } = null!;
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? Type { get; init; }
}
