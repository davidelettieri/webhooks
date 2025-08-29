namespace StandardWebhooks;

public sealed class WebhookValidationFilterOptions
{
    /// <summary>
    /// Symmetric secret used to verify webhook signatures (implementation-specific).
    /// </summary>
    public string Key { get; set; } = string.Empty;
}