using Microsoft.AspNetCore.Http;

namespace Webhooks.Receivers;

public static class HttpContextExtensions
{
    internal static void SetWebhookHeader(this HttpContext context, WebhookHeader header)
    {
        context.Items[typeof(WebhookHeader)] = header;
    }

    public static WebhookHeader? GetWebhookHeader(this HttpContext context)
    {
        return context.Items.TryGetValue(typeof(WebhookHeader), out var value) && value is WebhookHeader header
            ? header
            : null;
    }
}
