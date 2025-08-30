using Microsoft.AspNetCore.Http;

namespace Webhooks.Receivers;

public interface IValidationWebhookKeyRetriever
{
    byte[] GetKey(HttpContext context);
}
