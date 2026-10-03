namespace ScrumPilot.Shared.Models;

/// <summary>Role assigned to a user within an organization.</summary>
public enum OrganizationRole
{
    /// <summary>Can administer the organization and has implicit access to its projects.</summary>
    Owner,

    /// <summary>Can access organization projects explicitly granted to them.</summary>
    Member
}
