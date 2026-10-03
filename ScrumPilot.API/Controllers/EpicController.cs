using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EpicController(
    IEpicService epicService,
    IEpicRepository epicRepository,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Epic>>> GetAllEpics(
        [FromQuery] int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();
        return Ok(await epicService.GetEpicsByProjectAsync(projectId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<Epic>> Create(
        [FromQuery] int projectId,
        [FromBody] Epic request,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();
        return Ok(await epicService.CreateAsync(new Epic
        {
            ProjectId = projectId,
            Name = request.Name,
            DateCreated = DateTime.UtcNow
        }, cancellationToken));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<Epic>> Update(
        int id,
        [FromBody] Epic request,
        CancellationToken cancellationToken = default)
    {
        if (id != request.EpicId) return BadRequest();
        var existing = await epicRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null || !await CanAccess(existing.ProjectId, cancellationToken)) return NotFound();
        existing.Name = request.Name;
        return Ok(await epicService.UpdateAsync(existing, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var existing = await epicRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null || !await CanAccess(existing.ProjectId, cancellationToken)) return NotFound();
        await epicService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private Task<bool> CanAccess(int projectId, CancellationToken cancellationToken) =>
        accessService.CanAccessProjectAsync(currentUser.UserId, projectId, cancellationToken);
}
