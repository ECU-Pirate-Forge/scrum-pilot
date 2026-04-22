namespace ScrumPilot.Shared.Models;

/// <summary>
/// Stores a user's per-project dashboard widget layout as a JSON blob.
/// </summary>
public class UserDashboardPreference
{
    /// <summary>The Identity ID of the user who owns these preferences.</summary>
    public string UserId { get; set; } = "";

    /// <summary>The project these preferences apply to.</summary>
    public int ProjectId { get; set; }

    /// <summary>JSON-serialised <see cref="DashboardPreferenceDto"/> payload.</summary>
    public string? PreferencesJson { get; set; }
}
