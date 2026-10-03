namespace ScrumPilot.Shared.Models;

/// <summary>Associates a user with an organization and a role.</summary>
public class OrganizationMembership
{
    /// <summary>Identifier of the organization the user belongs to.</summary>
    public int OrganizationId { get; set; }

    /// <summary>Identity identifier of the member.</summary>
    public required string UserId { get; set; }

    /// <summary>The member's role within the organization.</summary>
    public required OrganizationRole Role { get; set; }

    /// <summary>UTC date and time when the user joined the organization.</summary>
    public DateTime JoinedAt { get; set; }

    /// <summary>Organization the membership belongs to.</summary>
    public Organization? Organization { get; set; }
}
