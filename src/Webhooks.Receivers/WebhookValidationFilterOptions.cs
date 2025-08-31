namespace Webhooks.Receivers;

public sealed class WebhookValidationFilterOptions
{
    /// <summary>
    /// Symmetric secret used to verify webhook signatures (implementation-specific).
    /// </summary>
    public string Key { get; init; } = string.Empty;
}
