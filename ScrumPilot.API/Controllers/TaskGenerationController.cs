using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:int}/projects/{projectId:int}/task-generation")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class TaskGenerationController(ICurrentUser user, IOrganizationAccessService access,
    TaskGenerationService generation) : ProjectPlanningControllerBase(user, access)
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(int projectId, GenerateTaskPreviewRequest request, CancellationToken ct) =>
        !await CanRead(projectId, ct) ? NotFound() : Ok(await generation.PreviewAsync(projectId, request, Actor, ct));
    [HttpPost("accept")]
    public async Task<IActionResult> Accept(int projectId, AcceptTaskProposalsRequest request, CancellationToken ct) =>
        !await CanRead(projectId, ct) ? NotFound() : Ok(await generation.AcceptAsync(projectId, request, Actor, ct));
}
