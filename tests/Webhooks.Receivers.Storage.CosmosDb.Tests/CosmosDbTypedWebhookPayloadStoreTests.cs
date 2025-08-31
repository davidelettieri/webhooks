using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text.Json;

namespace Webhooks.Receivers.Storage.CosmosDb.Tests;

[Collection("cosmos-emulator")]
public sealed class CosmosDbTypedWebhookPayloadStoreTests(CosmosEmulatorFixture fixture)
{
    private readonly CosmosEmulatorFixture _fixture = fixture;

    [Fact]
    public async Task StorePayloadAsync_TypedPayload_Persists_With_Type_And_SerializedJson()
    {
        // Arrange
        var options = Options.Create(new CosmosDbWebhookPayloadStoreOptions
        {
            Database = "webhooks",
            Container = "payloads"
        });

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var store = new CosmosDbWebhookPayloadStore(options, _fixture.Client, serializerOptions);

        var id = Guid.NewGuid().ToString("N");
        var payload = new TestPayload("abc", 42);
        var receivedAt = DateTimeOffset.UtcNow;

        var expectedJson = System.Text.Json.JsonSerializer.Serialize(payload, serializerOptions);
        var expectedType = typeof(TestPayload).FullName;

        // Act
        await store.StorePayloadAsync(id, receivedAt, payload);

        // Assert - read back from container
        var container = _fixture.Client.GetContainer("webhooks", "payloads");
        var response = await container.ReadItemAsync<PayloadDoc>(id, new PartitionKey(id));

        Assert.Equal(id, response.Resource.Id);
        Assert.Equal(expectedJson, response.Resource.Payload);
        Assert.Equal(expectedType, response.Resource.Type);
        Assert.True((response.Resource.ReceivedAt - receivedAt).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task StorePayloadAsync_StringPayload_IsSerialized_AsJsonString_And_HasStringType()
    {
        // Arrange
        var options = Options.Create(new CosmosDbWebhookPayloadStoreOptions
        {
            Database = "webhooks",
            Container = "payloads"
        });

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var store = new CosmosDbWebhookPayloadStore(options, _fixture.Client, serializerOptions);

        var id = Guid.NewGuid().ToString("N");
        var payload = "hello world";
        var receivedAt = DateTimeOffset.UtcNow;

        // System.Text.Json serializes string payloads with quotes
        var expectedJson = System.Text.Json.JsonSerializer.Serialize(payload, serializerOptions);
        var expectedType = typeof(string).FullName;

        // Act
        await store.StorePayloadAsync(id, receivedAt, payload);

        // Assert
        var container = _fixture.Client.GetContainer("webhooks", "payloads");
        var response = await container.ReadItemAsync<PayloadDoc>(id, new PartitionKey(id));

        Assert.Equal(expectedJson, response.Resource.Payload);
        Assert.Equal(expectedType, response.Resource.Type);
    }

    [Fact]
    public async Task StorePayloadAsync_CancellationRequested_Throws_OperationCanceled()
    {
        // Arrange
        var options = Options.Create(new CosmosDbWebhookPayloadStoreOptions
        {
            Database = "webhooks",
            Container = "payloads"
        });

        var serializerOptions = new JsonSerializerOptions();
        var store = new CosmosDbWebhookPayloadStore(options, _fixture.Client, serializerOptions);

        var id = Guid.NewGuid().ToString("N");
        var payload = new TestPayload("cancel", 1);
        var receivedAt = DateTimeOffset.UtcNow;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act + Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.StorePayloadAsync(id, receivedAt, payload, cts.Token));
    }

    private sealed record TestPayload(string Foo, int Bar);

    private sealed class PayloadDoc
    {
        [JsonProperty("id")]
        public string Id { get; set; } = null!;

        public string Payload { get; set; } = null!;

        public DateTimeOffset ReceivedAt { get; set; }

        public string? Type { get; set; }
    }
}
