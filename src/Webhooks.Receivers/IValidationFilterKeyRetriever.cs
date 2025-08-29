using Microsoft.AspNetCore.Http;

namespace Webhooks.Receivers;

public interface IValidationFilterKeyRetriever
{
    byte[] GetKey(EndpointFilterInvocationContext context);
}
