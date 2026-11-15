using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:int}/projects/{projectId:int}/story-generation")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class ProjectStoryGenerationController(ProjectStoryGenerationService service,
    ICurrentUser user, IOrganizationAccessService access) : ProjectPlanningControllerBase(user, access)
{
    [HttpPost]
    public async Task<IActionResult> Generate(int projectId, GenerateProjectStoriesRequest request, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.GenerateAsync(projectId, request, Actor, ct)) : NotFound();

    [HttpGet]
    public async Task<IActionResult> Runs(int projectId, [FromQuery] int? statementId, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.RunsAsync(projectId, statementId, ct)) : NotFound();

    [HttpGet("{runId:int}")]
    public async Task<IActionResult> Run(int projectId, int runId, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.RunAsync(projectId, runId, ct)) : NotFound();

    [HttpPost("accept")]
    public async Task<IActionResult> Accept(int projectId, AcceptProjectStoriesRequest request, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.AcceptAsync(projectId, request, ct)) : NotFound();

    [HttpPost("stories/{pbiId:int}/link-source")]
    public async Task<IActionResult> Link(int projectId, int pbiId, LinkLegacyStoryRequest request, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.LinkLegacyAsync(projectId, pbiId, request, ct)) : NotFound();

    [HttpGet("stories/{pbiId:int}/currency")]
    public async Task<IActionResult> Currency(int projectId, int pbiId, CancellationToken ct) =>
        await CanRead(projectId, ct) ? Ok(await service.CurrencyAsync(projectId, pbiId, ct)) : NotFound();

    [HttpPost("stories/{pbiId:int}/review")]
    public async Task<IActionResult> Review(int projectId, int pbiId, ReviewStoryApplicabilityRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        if (!await CanManage(projectId, ct)) return Forbid();
        return Ok(await service.ReviewApplicabilityAsync(projectId, pbiId, request, Actor, ct));
    }
}
