using System.Text;
using Microsoft.Extensions.Options;

namespace Webhooks.Publishers;

public sealed class OptionsKeyRetriever(IOptions<WebhookPublisherOptions> options) : IPublisherKeyRetriever
{
    private readonly byte[] _keyBytes = Encoding.UTF8.GetBytes(options.Value.Key);

    // Interpret the configured key as UTF-8 bytes.
    public byte[] GetKey() => _keyBytes;
}

public sealed class WebhookPublisherOptions
{
    /// <summary>
    /// Symmetric secret used to verify webhook signatures (implementation-specific).
    /// </summary>
    public required string Key { get; init; }
}