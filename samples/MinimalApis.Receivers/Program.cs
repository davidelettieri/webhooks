using Webhooks.Receivers;
using WebHooks.Receivers.Storage;
using Webhooks.Receivers.Storage.CosmosDb.Registry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddAzureCosmosClient(
    connectionName: "cosmos-db"); // Configure a dev/test secret. Replace with configuration or secrets in production.

// Dependencies required by the filter
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<WebhookValidationFilterOptions>(builder.Configuration.GetSection("WebhookValidationFilter"));

// Provide an IKeyRetriever that returns the symmetric key bytes
builder.Services.AddSingleton<IValidationWebhookKeyRetriever, OptionsKeyRetriever>();

// Add storage
builder.Services.AddCosmosDbWebhookPayloadStorage();

var app = builder.Build();

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/webhooks"),
    configuration => configuration.UseMiddleware<SymmetricKeyWebhookValidationMiddleware>());

app.MapDefaultEndpoints();

// Group all webhook endpoints and attach the validation filter
var webhooks = app.MapGroup("/webhooks");

// Sample webhook endpoint
webhooks.MapPost("/receive",
    async (
        HttpRequest req,
        WebhookHeader header,
        IWebhookPayloadStore store,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>

    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        logger.LogInformation(body);
        await store.StorePayloadAsync(header.Id, header.Timestamp, body, cancellationToken);
        return Results.Ok(new { received = true, length = body.Length, body });
    });

app.MapGet("/", () => "Running!");

app.Run();

public partial class Program;