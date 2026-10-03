using System.Text.Json.Serialization;

namespace ScrumPilot.Shared.Models;

/// <summary>Request to grant or revoke a user's explicit project access.</summary>
/// <param name="UserId">Identity identifier of the affected user.</param>
/// <param name="HasAccess">Whether the user should have explicit project access.</param>
public record SetProjectAccessRequest(
    string UserId,
    [property: JsonRequired] bool HasAccess);
