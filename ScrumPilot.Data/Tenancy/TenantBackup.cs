namespace ScrumPilot.Data.Tenancy;

/// <summary>Central authoritative registry; caller-supplied paths are never accepted.</summary>
public sealed class TenantBackup
{
    public required string BackupId { get; set; }
    public int OrganizationId { get; set; }
    public required string StorageKey { get; set; }
    public required string Sha256 { get; set; }
    public required string MigrationsJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
