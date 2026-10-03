using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SprintController(
    ISprintService sprintService,
    ISprintRepository sprintRepository,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Sprint>>> GetAllSprints(
        [FromQuery] int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();
        return Ok(await sprintService.GetSprintsByProjectAsync(projectId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<Sprint>> Create(
        [FromQuery] int projectId,
        [FromBody] Sprint request,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();
        var sprint = new Sprint { ProjectId = projectId };
        ApplyMutableFields(sprint, request);
        return Ok(await sprintService.CreateAsync(sprint, cancellationToken));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Sprint>> Update(
        int id,
        [FromBody] Sprint request,
        CancellationToken cancellationToken = default)
    {
        if (id != request.SprintId) return BadRequest();
        var existing = await sprintRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null || !await CanAccess(existing.ProjectId, cancellationToken)) return NotFound();
        ApplyMutableFields(existing, request);
        return Ok(await sprintService.UpdateAsync(existing, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var existing = await sprintRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null || !await CanAccess(existing.ProjectId, cancellationToken)) return NotFound();
        await sprintService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private Task<bool> CanAccess(int projectId, CancellationToken cancellationToken) =>
        accessService.CanAccessProjectAsync(currentUser.UserId, projectId, cancellationToken);

    private static void ApplyMutableFields(Sprint target, Sprint source)
    {
        target.SprintTitle = source.SprintTitle;
        target.SprintGoal = source.SprintGoal;
        target.StartDate = source.StartDate;
        target.EndDate = source.EndDate;
        target.IsOpen = source.IsOpen;
        target.DateClosed = source.DateClosed;
    }
}
