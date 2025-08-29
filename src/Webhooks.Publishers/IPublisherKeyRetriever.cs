using Microsoft.AspNetCore.Http;

namespace Webhooks.Publishers;

public interface IPublisherKeyRetriever
{
    byte[] GetKey();
}