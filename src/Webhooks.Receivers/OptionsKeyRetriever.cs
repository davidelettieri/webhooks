using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Webhooks.Receivers;

public sealed class OptionsKeyRetriever(IOptions<WebhookValidationFilterOptions> options) : IValidationWebhookKeyRetriever
{
    private readonly byte[] _keyBytes = Encoding.UTF8.GetBytes(options.Value.Key);

    // Interpret the configured key as UTF-8 bytes.
    public byte[] GetKey(HttpContext context) => _keyBytes;
}
