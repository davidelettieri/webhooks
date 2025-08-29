using StandardWebhooks;

var builder = WebApplication.CreateBuilder(args);

// Dependencies required by the filter
builder.Services.AddSingleton(TimeProvider.System);

// Configure a dev/test secret. Replace with configuration or secrets in production.
builder.Services.Configure<WebhookValidationFilterOptions>(opts =>
{
    opts.Key = "whsec_test_123";
});

// Provide an IKeyRetriever that returns the symmetric key bytes
builder.Services.AddSingleton<IKeyRetriever, OptionsKeyRetriever>();

var app = builder.Build();

// Group all webhook endpoints and attach the validation filter
var webhooks = app.MapGroup("/webhooks")
    .AddEndpointFilter<SymmetricKeyWebhookValidationFilter>();

// Sample webhook endpoint
webhooks.MapPost("/receive", async (HttpRequest req) =>
{
    using var reader = new StreamReader(req.Body);
    var body = await reader.ReadToEndAsync();
    return Results.Ok(new { received = true, length = body.Length, body });
});

app.MapGet("/", () => "Running!");

app.Run();