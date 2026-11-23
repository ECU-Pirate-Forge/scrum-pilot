using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:int}/projects/{projectId:int}/task-creation")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class TaskCreationController(
    ICurrentUser user,
    IOrganizationAccessService access,
    TaskCreationService tasks) : ProjectPlanningControllerBase(user, access)
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(int projectId, GenerateTaskCreationPreviewRequest request, CancellationToken ct) =>
        !await CanRead(projectId, ct) ? NotFound() : Ok(await tasks.PreviewAsync(projectId, request, Actor, ct));

    [HttpPost("accept")]
    public async Task<IActionResult> Accept(int projectId, AcceptTaskCreationProposalsRequest request, CancellationToken ct) =>
        !await CanRead(projectId, ct) ? NotFound() : Ok(await tasks.AcceptAsync(projectId, request, Actor, ct));

    [HttpPost]
    public async Task<IActionResult> CreateManual(int projectId, CreateManualChildTaskRequest request, CancellationToken ct) =>
        !await CanRead(projectId, ct) ? NotFound() : Ok(await tasks.CreateManualAsync(projectId, request, Actor, ct));

    [HttpDelete("{taskId:int}")]
    public async Task<IActionResult> Delete(int projectId, int taskId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        await tasks.DeleteAsync(projectId, taskId, ct);
        return NoContent();
    }
}
