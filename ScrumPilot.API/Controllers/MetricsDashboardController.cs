using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/metrics")]
public class MetricsDashboardController : ControllerBase
{
    private readonly IMetricsDashboardService _svc;

    public MetricsDashboardController(IMetricsDashboardService svc)
    {
        _svc = svc;
    }

    [HttpGet("sprint-summary/{sprintId:int}")]
    public async Task<ActionResult<SprintSummaryDto>> GetSprintSummary(int sprintId)
    {
        var result = await _svc.GetSprintSummaryAsync(sprintId);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("sprint-progress/{sprintId:int}")]
    public async Task<ActionResult<SprintProgressDto>> GetSprintProgress(int sprintId)
        => Ok(await _svc.GetSprintProgressAsync(sprintId));

    [HttpGet("burndown/{sprintId:int}")]
    public async Task<ActionResult<List<BurndownPoint>>> GetBurndown(int sprintId)
        => Ok(await _svc.GetBurndownDataAsync(sprintId));

    [HttpGet("velocity")]
    public async Task<ActionResult<List<VelocityPoint>>> GetVelocity([FromQuery] int? sprintId = null, [FromQuery] int? projectId = null)
        => Ok(await _svc.GetVelocityDataAsync(sprintId, projectId));

    [HttpGet("wip/{sprintId:int}")]
    public async Task<ActionResult<List<WipItem>>> GetWip(int sprintId)
        => Ok(await _svc.GetWipItemsAsync(sprintId));

    [HttpGet("bug-trend/{sprintId:int}")]
    public async Task<ActionResult<List<BugTrendPoint>>> GetBugTrend(int sprintId)
        => Ok(await _svc.GetBugTrendAsync(sprintId));

    [HttpGet("cycle-time/{sprintId:int}")]
    public async Task<ActionResult<List<CycleTimePoint>>> GetCycleTime(int sprintId)
        => Ok(await _svc.GetCycleTimeDataAsync(sprintId));

    [HttpGet("work-by-status/{sprintId:int}")]
    public async Task<ActionResult<List<WorkByStatusPoint>>> GetWorkByStatus(int sprintId)
        => Ok(await _svc.GetWorkByStatusAsync(sprintId));

    [HttpGet("time-in-stage/{sprintId:int}")]
    public async Task<ActionResult<TimeInStageData>> GetTimeInStage(int sprintId)
        => Ok(await _svc.GetTimeInStageDataAsync(sprintId));
}
