using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Webhooks.Receivers.Storage.CosmosDb.Tests;

[Collection("cosmos-emulator")]
public sealed class CosmosDbWebhookPayloadStoreTests(CosmosEmulatorFixture fixture)
{
    private readonly CosmosEmulatorFixture _fixture = fixture;

    [Fact]
    public async Task StorePayloadAsync_inserts_document()
    {
        // Arrange
        var options = Options.Create(new CosmosDbWebhookPayloadStoreOptions
        {
            Database = "webhooks",
            Container = "payloads"
        });

        var store = new CosmosDbSimpleWebhookPayloadStore(options, _fixture.Client);

        var id = Guid.NewGuid().ToString("N");
        var payload = """{"hello":"world"}""";
        var receivedAt = DateTimeOffset.UtcNow;

        // Act
        await store.StorePayloadAsync(id, receivedAt, payload);

        // Assert - read back from container
        var container = _fixture.Client.GetContainer("webhooks", "payloads");
        var response = await container.ReadItemAsync<PayloadDoc>(id, new PartitionKey(id));

        Assert.Equal(id, response.Resource.Id);
        Assert.Equal(payload, response.Resource.Payload);
        // Allow a small delta on timestamp
        Assert.True((response.Resource.ReceivedAt - receivedAt).Duration() < TimeSpan.FromMinutes(1));
    }

    private sealed class PayloadDoc
    {
        [JsonProperty("id")]
        public string Id { get; set; } = null!;

        public string Payload { get; set; } = null!;

        public DateTimeOffset ReceivedAt { get; set; }
    }
}
