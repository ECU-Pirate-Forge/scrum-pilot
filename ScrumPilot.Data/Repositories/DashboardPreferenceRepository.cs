using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public class DashboardPreferenceRepository : IDashboardPreferenceRepository
{
    private readonly ScrumPilotContext _ctx;

    public DashboardPreferenceRepository(ScrumPilotContext ctx) => _ctx = ctx;

    public async Task<string?> GetPreferencesJsonAsync(string userId, int projectId)
    {
        var pref = await _ctx.UserDashboardPreferences
            .FindAsync(userId, projectId);
        return pref?.PreferencesJson;
    }

    public async Task UpsertPreferencesJsonAsync(string userId, int projectId, string json)
    {
        var existing = await _ctx.UserDashboardPreferences
            .FindAsync(userId, projectId);
        if (existing is null)
        {
            _ctx.UserDashboardPreferences.Add(new UserDashboardPreference
            {
                UserId = userId,
                ProjectId = projectId,
                PreferencesJson = json
            });
        }
        else
        {
            existing.PreferencesJson = json;
        }
        await _ctx.SaveChangesAsync();
    }
}
