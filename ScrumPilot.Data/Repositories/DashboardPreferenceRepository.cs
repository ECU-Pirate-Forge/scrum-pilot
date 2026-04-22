using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public class DashboardPreferenceRepository : IDashboardPreferenceRepository
{
    private readonly ScrumPilotContext _ctx;

    public DashboardPreferenceRepository(ScrumPilotContext ctx) => _ctx = ctx;

    public async Task<string?> GetPreferencesJsonAsync(string userId)
    {
        var pref = await _ctx.UserDashboardPreferences.FindAsync(userId);
        return pref?.PreferencesJson;
    }

    public async Task UpsertPreferencesJsonAsync(string userId, string json)
    {
        var existing = await _ctx.UserDashboardPreferences.FindAsync(userId);
        if (existing is null)
        {
            _ctx.UserDashboardPreferences.Add(new UserDashboardPreference
            {
                UserId = userId,
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
