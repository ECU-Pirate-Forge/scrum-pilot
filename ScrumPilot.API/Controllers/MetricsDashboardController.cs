using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/metrics")]
public class MetricsDashboardController(
    IMetricsDashboardService service,
    ISprintRepository sprintRepository,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService) : ControllerBase
{
    [HttpGet("sprint-summary/{sprintId:int}")]
    public async Task<ActionResult<SprintSummaryDto>> GetSprintSummary(int sprintId)
    {
        if (!await CanAccessSprint(sprintId)) return NotFound();
        var result = await service.GetSprintSummaryAsync(sprintId);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("sprint-progress/{sprintId:int}")]
    public async Task<ActionResult<SprintProgressDto>> GetSprintProgress(int sprintId) =>
        await SprintMetric(sprintId, service.GetSprintProgressAsync);

    [HttpGet("burndown/{sprintId:int}")]
    public async Task<ActionResult<List<BurndownPoint>>> GetBurndown(int sprintId) =>
        await SprintMetric(sprintId, service.GetBurndownDataAsync);

    [HttpGet("velocity")]
    public async Task<ActionResult<List<VelocityPoint>>> GetVelocity(
        [FromQuery] int projectId,
        [FromQuery] int? sprintId = null)
    {
        if (!await accessService.CanAccessProjectAsync(currentUser.UserId, projectId))
            return NotFound();
        if (sprintId.HasValue
            && !await accessService.SprintBelongsToProjectAsync(sprintId.Value, projectId))
            return NotFound();
        return Ok(await service.GetVelocityDataAsync(sprintId, projectId));
    }

    [HttpGet("wip/{sprintId:int}")]
    public async Task<ActionResult<List<WipItem>>> GetWip(int sprintId) =>
        await SprintMetric(sprintId, service.GetWipItemsAsync);

    [HttpGet("bug-trend/{sprintId:int}")]
    public async Task<ActionResult<List<BugTrendPoint>>> GetBugTrend(int sprintId) =>
        await SprintMetric(sprintId, service.GetBugTrendAsync);

    [HttpGet("cycle-time/{sprintId:int}")]
    public async Task<ActionResult<List<CycleTimePoint>>> GetCycleTime(int sprintId) =>
        await SprintMetric(sprintId, service.GetCycleTimeDataAsync);

    [HttpGet("work-by-status/{sprintId:int}")]
    public async Task<ActionResult<List<WorkByStatusPoint>>> GetWorkByStatus(int sprintId) =>
        await SprintMetric(sprintId, service.GetWorkByStatusAsync);

    [HttpGet("time-in-stage/{sprintId:int}")]
    public async Task<ActionResult<TimeInStageData>> GetTimeInStage(int sprintId) =>
        await SprintMetric(sprintId, service.GetTimeInStageDataAsync);

    private async Task<ActionResult<T>> SprintMetric<T>(int sprintId, Func<int, Task<T>> load)
    {
        if (!await CanAccessSprint(sprintId)) return NotFound();
        return Ok(await load(sprintId));
    }

    private async Task<bool> CanAccessSprint(int sprintId)
    {
        var sprint = await sprintRepository.GetByIdAsync(sprintId);
        return sprint is not null
            && await accessService.CanAccessProjectAsync(currentUser.UserId, sprint.ProjectId);
    }
}
