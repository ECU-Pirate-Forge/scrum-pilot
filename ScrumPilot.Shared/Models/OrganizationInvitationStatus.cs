namespace ScrumPilot.Shared.Models;

/// <summary>Lifecycle state of an organization invitation.</summary>
public enum OrganizationInvitationStatus
{
    /// <summary>The invitation can be accepted.</summary>
    Pending,

    /// <summary>The invitation has been accepted.</summary>
    Accepted,

    /// <summary>The invitation was revoked before acceptance.</summary>
    Revoked,

    /// <summary>The invitation expired before acceptance.</summary>
    Expired
}
