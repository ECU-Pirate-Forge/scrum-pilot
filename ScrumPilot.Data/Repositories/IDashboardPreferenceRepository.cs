namespace ScrumPilot.Data.Repositories;

/// <summary>
/// Data-access contract for persisting user dashboard layout preferences as JSON blobs.
/// </summary>
public interface IDashboardPreferenceRepository
{
    /// <summary>
    /// Returns the raw JSON preferences string for the given user and project,
    /// or <c>null</c> if no preferences have been saved yet.
    /// </summary>
    Task<string?> GetPreferencesJsonAsync(string userId, int projectId);

    /// <summary>Inserts or updates the JSON preferences for the given user and project.</summary>
    Task UpsertPreferencesJsonAsync(string userId, int projectId, string json);
}
