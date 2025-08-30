using Microsoft.AspNetCore.Http;

namespace Webhooks.Receivers.Tests;

internal sealed class FixedValidationWebhookKeyRetriever(byte[] key)
    : IValidationWebhookKeyRetriever
{
    public byte[] GetKey(HttpContext context) => key;
}