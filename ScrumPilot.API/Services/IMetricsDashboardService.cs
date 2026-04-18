using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public interface IMetricsDashboardService
{
    Task<SprintSummaryDto?> GetSprintSummaryAsync(int sprintId);
    Task<SprintProgressDto> GetSprintProgressAsync(int sprintId);
    Task<List<BurndownPoint>> GetBurndownDataAsync(int sprintId);
    Task<List<VelocityPoint>> GetVelocityDataAsync(int? currentSprintId = null);
    Task<List<WipItem>> GetWipItemsAsync(int sprintId);
    Task<List<BugTrendPoint>> GetBugTrendAsync(int sprintId);
    Task<List<CycleTimePoint>> GetCycleTimeDataAsync(int sprintId);
    Task<List<WorkByStatusPoint>> GetWorkByStatusAsync(int sprintId);
    Task<TimeInStageData> GetTimeInStageDataAsync(int sprintId);
}
