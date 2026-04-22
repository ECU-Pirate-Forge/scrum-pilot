using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>
/// Business-logic contract for persisting per-user dashboard layout preferences.
/// </summary>
public interface IDashboardPreferenceService
{
    /// <summary>Returns the saved dashboard preferences for the given user and project.</summary>
    Task<DashboardPreferenceDto> GetPreferencesAsync(string userId, int projectId);

    /// <summary>Saves (insert or update) the dashboard preferences for the given user and project.</summary>
    Task SavePreferencesAsync(string userId, int projectId, DashboardPreferenceDto dto);
}
