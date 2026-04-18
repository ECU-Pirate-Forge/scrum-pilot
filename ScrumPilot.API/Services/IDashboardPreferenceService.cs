using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public interface IDashboardPreferenceService
{
    Task<DashboardPreferenceDto> GetPreferencesAsync(string userId);
    Task SavePreferencesAsync(string userId, DashboardPreferenceDto dto);
}
