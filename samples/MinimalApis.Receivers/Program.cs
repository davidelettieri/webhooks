using Webhooks.Receivers;
using Webhooks.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Dependencies required by the filter
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<WebhookValidationFilterOptions>(builder.Configuration.GetSection("WebhookValidationFilter"));

// Provide an IKeyRetriever that returns the symmetric key bytes
builder.Services.AddSingleton<IValidationWebhookKeyRetriever, OptionsKeyRetriever>();

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
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>

    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        logger.LogInformation(body);
        var header = context.GetWebhookHeader();

        if (header == null)
        {
            return Results.BadRequest(new { error = "Could not parse webhook." });
        }

        return Results.Ok(new { received = true, length = body.Length, body });
    });

app.MapGet("/", () => "Running!");

app.Run();
