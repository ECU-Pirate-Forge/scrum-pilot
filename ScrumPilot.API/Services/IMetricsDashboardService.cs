using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>
/// Business-logic contract for computing sprint and project metric data
/// consumed by the metrics dashboard widgets.
/// </summary>
public interface IMetricsDashboardService
{
    /// <summary>
    /// Returns a high-level summary for the given sprint, or <c>null</c> if the sprint does not exist.
    /// </summary>
    Task<SprintSummaryDto?> GetSprintSummaryAsync(int sprintId);

    /// <summary>Returns committed vs. completed story-point progress for the given sprint.</summary>
    Task<SprintProgressDto> GetSprintProgressAsync(int sprintId);

    /// <summary>Returns daily ideal and actual burndown data points for the given sprint.</summary>
    Task<List<BurndownPoint>> GetBurndownDataAsync(int sprintId);

    /// <summary>
    /// Returns velocity data across completed sprints, optionally scoped to a specific
    /// sprint or project.
    /// </summary>
    Task<List<VelocityPoint>> GetVelocityDataAsync(int? currentSprintId = null, int? projectId = null);

    /// <summary>Returns all in-progress PBIs for the given sprint as WIP table rows.</summary>
    Task<List<WipItem>> GetWipItemsAsync(int sprintId);

    /// <summary>Returns daily bug creation and resolution counts for the given sprint.</summary>
    Task<List<BugTrendPoint>> GetBugTrendAsync(int sprintId);

    /// <summary>Returns average cycle-time data points for the given sprint.</summary>
    Task<List<CycleTimePoint>> GetCycleTimeDataAsync(int sprintId);

    /// <summary>Returns story-point totals split by work type for each status column.</summary>
    Task<List<WorkByStatusPoint>> GetWorkByStatusAsync(int sprintId);

    /// <summary>Returns the time-in-stage heat-map data for the given sprint.</summary>
    Task<TimeInStageData> GetTimeInStageDataAsync(int sprintId);
}
