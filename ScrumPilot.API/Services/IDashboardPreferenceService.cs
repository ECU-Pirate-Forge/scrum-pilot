using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public interface IDashboardPreferenceService
{
    Task<DashboardPreferenceDto> GetPreferencesAsync(string userId, int projectId);
    Task SavePreferencesAsync(string userId, int projectId, DashboardPreferenceDto dto);
}
