namespace ScrumPilot.Shared.Models;

/// <summary>Tenant that groups users and Scrum projects.</summary>
public class Organization
{
    public string StorageKey { get; set; } = Guid.NewGuid().ToString("N");
    public string? CatalogName { get; set; }
    public bool CatalogRenamePending { get; set; }
    public string StorageState { get; set; } = "Provisioning";
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string? OperationError { get; set; }
    /// <summary>Durable restore fence: every activation must clear restored explicit grants first.</summary>
    public bool RestorePending { get; set; }
    /// <summary>Unique identifier for this organization.</summary>
    public int OrganizationId { get; set; }

    /// <summary>Display name of the organization.</summary>
    public required string Name { get; set; }

    /// <summary>Canonical name used for case-insensitive comparisons.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>UTC date and time when the organization was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC date and time when the organization was soft-deleted.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Projects owned by this organization.</summary>
    public ICollection<Project> Projects { get; set; } = [];

    /// <summary>Users belonging to this organization.</summary>
    public ICollection<OrganizationMembership> OrganizationMemberships { get; set; } = [];

    /// <summary>Concurrency token used to detect conflicting updates.</summary>
    public byte[] RowVersion { get; set; } = [];
}
