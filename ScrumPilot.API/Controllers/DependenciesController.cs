using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:int}/projects/{projectId:int}")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class DependenciesController(ICurrentUser user, IOrganizationAccessService access,
    ProjectDependencyService dependencies, OrganizationContext db) : ProjectPlanningControllerBase(user, access)
{
    [HttpGet("dependency-graph")]
    [HttpGet("sprints/{sprintId:int}/dependency-graph")]
    public async Task<ActionResult<DependencyGraphDto>> Get(int projectId, int? sprintId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        if (sprintId.HasValue && !await db.Sprints.AnyAsync(sprint => sprint.ProjectId == projectId && sprint.SprintId == sprintId, ct))
            return NotFound();
        return Ok(await dependencies.ReadAsync(projectId, sprintId, ct));
    }

    [HttpPut("dependencies")]
    public async Task<IActionResult> Set(int projectId, SetDependenciesRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        await dependencies.SetAsync(projectId, request, ct);
        return NoContent();
    }

    [HttpPost("dependencies/review")]
    public async Task<IActionResult> Review(int projectId, ReviewDependencyRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        await dependencies.ReviewAsync(projectId, request, Actor, ct);
        return NoContent();
    }

    [HttpGet("dependency-reviews")]
    public async Task<IActionResult> Reviews(int projectId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        return Ok(await db.DependencyReviews.AsNoTracking().Where(review => review.ProjectId == projectId
            && review.ReviewedRevision != review.InvalidationRevision).ToListAsync(ct));
    }
}
