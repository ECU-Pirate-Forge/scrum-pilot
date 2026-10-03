namespace ScrumPilot.Shared.Models;

/// <summary>Invitation for an email address to join an organization.</summary>
public class OrganizationInvitation
{
    /// <summary>Unique identifier for this invitation.</summary>
    public int OrganizationInvitationId { get; set; }

    /// <summary>Identifier of the organization the recipient is invited to.</summary>
    public int OrganizationId { get; set; }

    /// <summary>Email address of the invitation recipient.</summary>
    public required string Email { get; set; }

    /// <summary>Canonical recipient email used for case-insensitive comparisons.</summary>
    public required string NormalizedEmail { get; set; }

    /// <summary>One-way hash of the invitation token.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Identity identifier of the user who created the invitation.</summary>
    public required string InvitedByUserId { get; set; }

    /// <summary>Role assigned when the invitation is accepted.</summary>
    public required OrganizationRole Role { get; set; }

    /// <summary>Current invitation lifecycle state.</summary>
    public OrganizationInvitationStatus Status { get; set; }

    /// <summary>UTC date and time when the invitation was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC date and time after which the invitation cannot be accepted.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>UTC date and time when the invitation was accepted.</summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>UTC date and time of the most recent delivery attempt.</summary>
    public DateTime? LastSentAt { get; set; }

    /// <summary>Error from the most recent failed delivery attempt.</summary>
    public string? DeliveryError { get; set; }
}
