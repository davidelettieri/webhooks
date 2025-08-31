using Microsoft.Extensions.DependencyInjection;
using WebHooks.Receivers.Storage;

namespace Webhooks.Receivers.Storage.CosmosDb.Registry;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCosmosDbWebhookPayloadStorage(
        this IServiceCollection services,
        Action<CosmosDbWebhookPayloadStoreOptions>? configure = null)
        => services
            .Configure(configure ?? (_ => { }))
            .AddSingleton<ISimpleWebhookPayloadStore, CosmosDbSimpleWebhookPayloadStore>();
}
