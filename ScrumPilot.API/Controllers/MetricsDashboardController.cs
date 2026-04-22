using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

/// <summary>
/// Provides computed sprint and project metrics consumed by the metrics dashboard widgets.
/// </summary>
[ApiController]
[Route("api/metrics")]
public class MetricsDashboardController : ControllerBase
{
    private readonly IMetricsDashboardService _svc;

    /// <summary>Initialises a new instance of <see cref="MetricsDashboardController"/>.</summary>
    public MetricsDashboardController(IMetricsDashboardService svc)
    {
        _svc = svc;
    }

    /// <summary>Returns a high-level summary for the given sprint, or 404 if not found.</summary>
    [HttpGet("sprint-summary/{sprintId:int}")]
    public async Task<ActionResult<SprintSummaryDto>> GetSprintSummary(int sprintId)
    {
        var result = await _svc.GetSprintSummaryAsync(sprintId);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Returns committed vs. completed story-point progress for the given sprint.</summary>
    [HttpGet("sprint-progress/{sprintId:int}")]
    public async Task<ActionResult<SprintProgressDto>> GetSprintProgress(int sprintId)
        => Ok(await _svc.GetSprintProgressAsync(sprintId));

    /// <summary>Returns daily ideal and actual burndown data points for the given sprint.</summary>
    [HttpGet("burndown/{sprintId:int}")]
    public async Task<ActionResult<List<BurndownPoint>>> GetBurndown(int sprintId)
        => Ok(await _svc.GetBurndownDataAsync(sprintId));

    /// <summary>Returns velocity data, optionally scoped to a sprint or project.</summary>
    [HttpGet("velocity")]
    public async Task<ActionResult<List<VelocityPoint>>> GetVelocity([FromQuery] int? sprintId = null, [FromQuery] int? projectId = null)
        => Ok(await _svc.GetVelocityDataAsync(sprintId, projectId));

    /// <summary>Returns all in-progress PBIs for the given sprint as WIP table rows.</summary>
    [HttpGet("wip/{sprintId:int}")]
    public async Task<ActionResult<List<WipItem>>> GetWip(int sprintId)
        => Ok(await _svc.GetWipItemsAsync(sprintId));

    /// <summary>Returns daily bug creation and resolution counts for the given sprint.</summary>
    [HttpGet("bug-trend/{sprintId:int}")]
    public async Task<ActionResult<List<BugTrendPoint>>> GetBugTrend(int sprintId)
        => Ok(await _svc.GetBugTrendAsync(sprintId));

    /// <summary>Returns average cycle-time data points for the given sprint.</summary>
    [HttpGet("cycle-time/{sprintId:int}")]
    public async Task<ActionResult<List<CycleTimePoint>>> GetCycleTime(int sprintId)
        => Ok(await _svc.GetCycleTimeDataAsync(sprintId));

    /// <summary>Returns story-point totals split by work type for each status column.</summary>
    [HttpGet("work-by-status/{sprintId:int}")]
    public async Task<ActionResult<List<WorkByStatusPoint>>> GetWorkByStatus(int sprintId)
        => Ok(await _svc.GetWorkByStatusAsync(sprintId));

    /// <summary>Returns time-in-stage heat-map data for the given sprint.</summary>
    [HttpGet("time-in-stage/{sprintId:int}")]
    public async Task<ActionResult<TimeInStageData>> GetTimeInStage(int sprintId)
        => Ok(await _svc.GetTimeInStageDataAsync(sprintId));
}
