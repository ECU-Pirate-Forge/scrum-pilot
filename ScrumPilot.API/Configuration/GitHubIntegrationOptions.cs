namespace ScrumPilot.API.Configuration;

public sealed class GitHubIntegrationOptions
{
    public const string SectionName = "GitHubIntegration";

    public bool Enabled { get; set; }
}
