using System.Text.Json.Serialization;

namespace ScrumPilot.Shared.Models;

/// <summary>Request to create a project inside the organization from the route.</summary>
public record CreateProjectRequest(string ProjectName, string? Description);

/// <summary>Request to update the mutable fields of a project.</summary>
public record UpdateProjectRequest(string ProjectName, string? Description);

/// <summary>Request to grant or revoke explicit access for the user from the route.</summary>
public record SetProjectAccessRequest([property: JsonRequired] bool HasAccess);

/// <summary>Organization membership and project-access details for a project member.</summary>
public record ProjectMemberAccessDto(
    string UserId,
    OrganizationRole OrganizationRole,
    bool HasExplicitAccess);
