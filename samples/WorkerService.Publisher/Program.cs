using Webhooks.Publishers;
using WorkerService.Publisher;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IWebhookPublisher, WebhookPublisher>();
builder.Services.Configure<WebhookPublisherOptions>(builder.Configuration.GetSection("WebhookPublisher"));
builder.Services.AddSingleton<IPublisherKeyRetriever, OptionsKeyRetriever>();
builder.Services.AddSingleton(TimeProvider.System);

var host = builder.Build();
host.Run();