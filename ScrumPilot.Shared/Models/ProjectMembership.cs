namespace ScrumPilot.Shared.Models;

/// <summary>Explicitly grants a user access to a project.</summary>
public class ProjectMembership
{
    /// <summary>Identifier of the accessible project.</summary>
    public int ProjectId { get; set; }

    /// <summary>Identity identifier of the user receiving access.</summary>
    public required string UserId { get; set; }

    /// <summary>UTC date and time when access was granted.</summary>
    public DateTime GrantedAt { get; set; }

    /// <summary>Identity identifier of the user who granted access.</summary>
    public required string GrantedByUserId { get; set; }
}
