using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/projects/{projectId:int}/guidelines")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class ProjectGuidelinesController(ProjectGuidelinesService service, ICurrentUser user,
    IOrganizationAccessService access) : ProjectPlanningControllerBase(user, access)
{
    [HttpGet]
    public async Task<IActionResult> Current(int projectId, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.CurrentAsync(projectId, ct)) : NotFound();

    [HttpGet("template")]
    public async Task<IActionResult> Template(int projectId, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(new { content = ProjectGuidelinesService.Template() }) : NotFound();

    [HttpGet("history")]
    public async Task<IActionResult> History(int projectId, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.HistoryAsync(projectId, ct)) : NotFound();

    [HttpPut]
    public async Task<IActionResult> Save(int projectId, SaveProjectGuidelinesRequest request, CancellationToken ct) =>
        await CanManage(projectId, ct) ? Ok(await service.SaveAsync(projectId, request, Actor, ct)) : NotFound();

    [HttpPost("{revisionId:int}/restore")]
    public async Task<IActionResult> Restore(int projectId, int revisionId, RestoreProjectGuidelinesRequest request, CancellationToken ct) =>
        await CanManage(projectId, ct) ? Ok(await service.RestoreAsync(projectId, revisionId, request, Actor, ct)) : NotFound();
}
