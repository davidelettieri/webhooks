using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Azure.Cosmos;
using System.Net;
using Testcontainers.CosmosDb;

namespace Webhooks.Receivers.Storage.CosmosDb.Tests;

public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    private IContainer? _container;

    public string Endpoint { get; private set; } = string.Empty;

    // Well-known emulator master key.
    public string Key { get; } = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public CosmosClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new CosmosDbBuilder()
          .WithImage("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest")
          .Build();

        await _container.StartAsync().ConfigureAwait(false);

        var port = _container.GetMappedPublicPort(8081);
        Endpoint = $"https://localhost:{port}/";

        // Bypass self-signed cert; safe in tests.
        Client = new CosmosClient(
            Endpoint,
            Key,
            new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                HttpClientFactory = () => new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                })
            });

        // Ensure database and container exist (match your store defaults)
        var dbResponse = await Client.CreateDatabaseIfNotExistsAsync("webhooks").ConfigureAwait(false);
        await dbResponse.Database.CreateContainerIfNotExistsAsync("payloads", "/id").ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        try
        {
            Client?.Dispose();
            if (_container is not null)
            {
                await _container.StopAsync().ConfigureAwait(false);
                await _container.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore cleanup errors in test teardown.
        }
    }
}

[CollectionDefinition("cosmos-emulator")]
public sealed class CosmosEmulatorCollection : ICollectionFixture<CosmosEmulatorFixture>
{
}