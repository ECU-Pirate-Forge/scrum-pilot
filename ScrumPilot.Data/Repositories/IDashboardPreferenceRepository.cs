namespace ScrumPilot.Data.Repositories;

public interface IDashboardPreferenceRepository
{
    Task<string?> GetPreferencesJsonAsync(string userId);
    Task UpsertPreferencesJsonAsync(string userId, string json);
}
