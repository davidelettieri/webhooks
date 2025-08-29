using Microsoft.AspNetCore.Http;

namespace StandardWebhooks;

public interface IKeyRetriever
{
    byte[] GetKey(EndpointFilterInvocationContext context);
}
