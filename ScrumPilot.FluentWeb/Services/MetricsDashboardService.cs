using System.Net.Http.Json;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.FluentWeb.Services;

public class MetricsDashboardService
{
    private readonly HttpClient _http;

    public MetricsDashboardService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<Project>> GetProjectsAsync()
    {
        return await _http.GetFromJsonAsync<List<Project>>("api/project") ?? [];
    }

    public async Task<List<Sprint>> GetSprintsByProjectAsync(int projectId)
    {
        return await _http.GetFromJsonAsync<List<Sprint>>($"api/sprint?projectId={projectId}") ?? [];
    }

    public async Task<List<ProductBacklogItem>> GetPbisForSprintAsync(int? sprintId, int? projectId = null)
    {
        if (!projectId.HasValue)
        {
            return [];
        }

        var url = $"api/pbi/getNonDraftPbis?projectId={projectId.Value}";
        if (sprintId.HasValue)
        {
            url += $"&sprintId={sprintId.Value}";
        }

        return await _http.GetFromJsonAsync<List<ProductBacklogItem>>(url) ?? [];
    }

    public async Task<SprintSummaryDto?> GetSprintSummaryAsync(int sprintId)
    {
        return await _http.GetFromJsonAsync<SprintSummaryDto>($"api/metrics/sprint-summary/{sprintId}");
    }

    public async Task<SprintProgressDto> GetSprintProgressAsync(int sprintId)
    {
        return await _http.GetFromJsonAsync<SprintProgressDto>($"api/metrics/sprint-progress/{sprintId}")
            ?? new SprintProgressDto(0, 0, 0, 0);
    }

    public async Task<List<BurndownPoint>> GetBurndownDataAsync(int sprintId)
    {
        return await _http.GetFromJsonAsync<List<BurndownPoint>>($"api/metrics/burndown/{sprintId}") ?? [];
    }

    public async Task<List<VelocityPoint>> GetVelocityDataAsync(int? sprintId = null, int? projectId = null)
    {
        if (!projectId.HasValue)
        {
            return [];
        }

        var qs = new List<string> { $"projectId={projectId.Value}" };
        if (sprintId.HasValue)
        {
            qs.Add($"sprintId={sprintId.Value}");
        }

        var url = $"api/metrics/velocity?{string.Join('&', qs)}";
        return await _http.GetFromJsonAsync<List<VelocityPoint>>(url) ?? [];
    }

    public async Task<List<WipItem>> GetWipItemsAsync(int sprintId)
    {
        return await _http.GetFromJsonAsync<List<WipItem>>($"api/metrics/wip/{sprintId}") ?? [];
    }

    public async Task<List<BugTrendPoint>> GetBugTrendAsync(int sprintId)
    {
        return await _http.GetFromJsonAsync<List<BugTrendPoint>>($"api/metrics/bug-trend/{sprintId}") ?? [];
    }

    public async Task<List<CycleTimePoint>> GetCycleTimeDataAsync(int sprintId)
    {
        return await _http.GetFromJsonAsync<List<CycleTimePoint>>($"api/metrics/cycle-time/{sprintId}") ?? [];
    }

    public async Task<List<WorkByStatusPoint>> GetWorkByStatusAsync(int sprintId)
    {
        return await _http.GetFromJsonAsync<List<WorkByStatusPoint>>($"api/metrics/work-by-status/{sprintId}") ?? [];
    }

    public async Task<TimeInStageData> GetTimeInStageDataAsync(int sprintId)
    {
        return await _http.GetFromJsonAsync<TimeInStageData>($"api/metrics/time-in-stage/{sprintId}")
            ?? new TimeInStageData();
    }

    public async Task<DashboardPreferenceDto?> GetDashboardPreferencesAsync(int projectId)
    {
        try
        {
            return await _http.GetFromJsonAsync<DashboardPreferenceDto>($"api/dashboard-preferences?projectId={projectId}");
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveDashboardPreferencesAsync(DashboardPreferenceDto dto, int projectId)
    {
        try
        {
            await _http.PutAsJsonAsync($"api/dashboard-preferences?projectId={projectId}", dto);
        }
        catch
        {
            // intentionally left blank in legacy service; rewritten to explicit error handling is deferred to later phase.
        }
    }
}
