namespace ScrumPilot.Shared.Models;

/// <summary>
/// Data transfer object for reading and updating a user's profile settings.
/// </summary>
public class UserSettingsDto
{
    /// <summary>The user's email address.</summary>
    public string? Email { get; set; }

    /// <summary>The user's Discord username for bot integrations.</summary>
    public string? DiscordUsername { get; set; }

    /// <summary>The user's preferred UI colour scheme.</summary>
    public UiPreference UiPreference { get; set; }

    /// <summary>The project ID to load automatically when the user logs in.</summary>
    public int? DefaultProjectId { get; set; }
}
