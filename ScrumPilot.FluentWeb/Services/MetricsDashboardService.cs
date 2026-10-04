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

    public async Task<IReadOnlyList<Project>> GetProjectsAsync()
    {
        var projects = await _http.GetFromJsonAsync<List<Project>>("api/projects");
        return projects ?? new List<Project>();
    }

    public async Task<IReadOnlyList<Sprint>> GetSprintsByProjectAsync(int projectId)
    {
        var sprints = await _http.GetFromJsonAsync<List<Sprint>>($"api/projects/{projectId}/sprints");
        return sprints ?? new List<Sprint>();
    }

    public async Task<DashboardMetrics?> GetDashboardMetricsAsync()
    {
        return await _http.GetFromJsonAsync<DashboardMetrics>("api/metrics/dashboard");
    }
}
