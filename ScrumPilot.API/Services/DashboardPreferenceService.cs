using System.Text.Json;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public class DashboardPreferenceService : IDashboardPreferenceService
{
    private readonly IDashboardPreferenceRepository _repo;

    public DashboardPreferenceService(IDashboardPreferenceRepository repo) => _repo = repo;

    public async Task<DashboardPreferenceDto> GetPreferencesAsync(string userId)
    {
        var json = await _repo.GetPreferencesJsonAsync(userId);
        if (string.IsNullOrEmpty(json)) return new DashboardPreferenceDto();
        return JsonSerializer.Deserialize<DashboardPreferenceDto>(json) ?? new DashboardPreferenceDto();
    }

    public async Task SavePreferencesAsync(string userId, DashboardPreferenceDto dto)
    {
        var json = JsonSerializer.Serialize(dto);
        await _repo.UpsertPreferencesJsonAsync(userId, json);
    }
}
