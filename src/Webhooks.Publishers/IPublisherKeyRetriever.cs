namespace Webhooks.Publishers;

public interface IPublisherKeyRetriever
{
    byte[] GetKey();
}
