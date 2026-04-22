namespace ScrumPilot.Shared.Models;

/// <summary>
/// Payload for the change-password endpoint.
/// </summary>
public class ChangePasswordRequest
{
    /// <summary>The user's existing password for verification.</summary>
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>The desired new password.</summary>
    public string NewPassword { get; set; } = string.Empty;
}
