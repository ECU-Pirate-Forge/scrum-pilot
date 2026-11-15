namespace ScrumPilot.API.Configuration;

public sealed class GitHubAppOptions
{
    public string AppId { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
}
