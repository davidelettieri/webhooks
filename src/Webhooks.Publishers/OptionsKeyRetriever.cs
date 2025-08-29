using System.Text;
using Microsoft.AspNetCore.Http;

namespace Webhooks.Publishers;

public sealed class OptionsKeyRetriever(string key) : IPublisherKeyRetriever
{
    private readonly byte[] _keyBytes = Encoding.UTF8.GetBytes(key);

    // Interpret the configured key as UTF-8 bytes.
    public byte[] GetKey() => _keyBytes;
}