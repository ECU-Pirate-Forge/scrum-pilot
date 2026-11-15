namespace ScrumPilot.Shared.Models;

public sealed record ConfigureProjectRepositoryRequest(long GitHubRepositoryId, long InstallationId,
    string Owner, string Name, string DefaultBranch, long ExpectedVersion);
public sealed record CreateRepositorySnapshotRequest(string Branch);
public sealed record ProjectRepositoryStatus(ProjectRepository? Repository, bool AppConfigured,
    string EnforcementStatus, bool WebhookConfigured = false);
