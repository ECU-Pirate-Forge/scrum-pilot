using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/projects/{projectId:int}/problem-statements")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class ProjectProblemStatementsController(ProjectStoryGenerationService service,
    ICurrentUser user, IOrganizationAccessService access) : ProjectPlanningControllerBase(user, access)
{
    [HttpGet]
    public async Task<IActionResult> List(int projectId, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.ProblemsAsync(projectId, ct)) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(int projectId, SaveProblemStatementRequest request, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.SaveProblemAsync(projectId, null, request, ct)) : NotFound();

    [HttpPut("{statementId:int}")]
    public async Task<IActionResult> Update(int projectId, int statementId, SaveProblemStatementRequest request, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.SaveProblemAsync(projectId, statementId, request, ct)) : NotFound();
}
