using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace StandardWebhooks;

public sealed class OptionsKeyRetriever(IOptions<WebhookValidationFilterOptions> options) : IKeyRetriever
{
    private readonly byte[] _keyBytes = Encoding.UTF8.GetBytes(options.Value.Key);

    // Interpret the configured key as UTF-8 bytes.
    // Change to Base64/Hex decode if your key is encoded.
    public byte[] GetKey(EndpointFilterInvocationContext context) => _keyBytes;
}