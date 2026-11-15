namespace ScrumPilot.Data.Models;

public enum RepositoryRegistrationState { Pending, Active, Suspended, Retiring, Retired }

public sealed class GitHubRepositoryRegistration
{
    public long GitHubRepositoryId { get; set; }
    public Guid RegistrationId { get; set; } = Guid.NewGuid();
    public int OrganizationId { get; set; }
    public required string StorageKey { get; set; }
    public int ProjectId { get; set; }
    public int ProjectRepositoryId { get; set; }
    public long InstallationId { get; set; }
    public required string Owner { get; set; }
    public required string Name { get; set; }
    public long ConfigurationVersion { get; set; }
    public RepositoryRegistrationState State { get; set; }
    public string? RecoveryError { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public byte[] Version { get; set; } = [];
}
