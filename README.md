# Webhooks

**Disclaimer:** This repository has been built with the support of github copilot GPT-5 preview. If you think AI produced some of your code verbatim, ping me and I'll remove the code, give you credit, or rectify licensing. Whatever you prefer. I do not claim to have written all the code in this repository and I don't intend to infringe on anyone's copyright.

This repository provides an implementation for signing webhooks payload and validating them. It is based on the https://github.com/standard-webhooks/standard-webhooks/blob/main/spec/standard-webhooks.md spec and compatible with https://www.nuget.org/packages/StandardWebhooks.

It does not provide storage, delivery or configuration mechanisms, as those are out of scope for this project at the moment.

The signing is required to ensure that the payload has not been tampered with in transit, and that it comes from a trusted source. It is based on a pre-shared secret between the sender and the receiver. If you are on the receiving end and the publisher uses a known IP address range, you can use that as an additional security measure.

Two packages are published to (github registry)[https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry], feel free to use them in your projects. A sample aspire project is included to demonstrate how to use the packages. The pre-share secret should be store securely, I provided two sample options retriever that recover the secret from the options pattern. You can use secrets to store the key or provide your own implementation to retrieve the secret from a secure location.

On the publisher side the interface for the key retriever is:

```csharp
public interface IPublisherKeyRetriever
{
    byte[] GetKey();
}
```

You can't pass any context to select a different key for different recipient. If you need that you can register multiple keyed publishers each one resolving a different keyed key retriever.

On the receiver side the interface for the key retriever is:

```csharp
public interface IValidationWebhookKeyRetriever
{
    byte[] GetKey(HttpContext context);
}
```

You can use the HttpContext to select a different key based on the request, for example you can use a different key based on the URL or a header. If that is not enough I suggest again to register multiple keyed validators each one resolving a different keyed key retriever.

I'm not a security expert, so please use this code at your own risk. If you find any issues or have any suggestions, please open an issue or a pull request.
