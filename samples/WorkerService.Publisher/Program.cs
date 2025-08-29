using Webhooks.Publishers;
using WorkerService.Publisher;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IWebhookPublisher, WebhookPublisher>();
builder.Services.AddSingleton<IPublisherKeyRetriever>(_ => new OptionsKeyRetriever("whsec_test_123"));
builder.Services.AddSingleton(TimeProvider.System);

var host = builder.Build();
host.Run();