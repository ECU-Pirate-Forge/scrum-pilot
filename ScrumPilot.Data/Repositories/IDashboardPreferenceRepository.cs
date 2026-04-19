namespace ScrumPilot.Data.Repositories;

public interface IDashboardPreferenceRepository
{
    Task<string?> GetPreferencesJsonAsync(string userId, int projectId);
    Task UpsertPreferencesJsonAsync(string userId, int projectId, string json);
}
