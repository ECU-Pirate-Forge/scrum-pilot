using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

/// <summary>
/// Manages per-user dashboard layout preferences for a given project.
/// </summary>
[ApiController]
[Route("api/dashboard-preferences")]
public class DashboardPreferenceController(
    IDashboardPreferenceService svc,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService) : ControllerBase
{
    /// <summary>Returns the authenticated user's saved dashboard preferences for the given project.</summary>
    [HttpGet]
    public async Task<ActionResult<DashboardPreferenceDto>> Get(
        [FromQuery] int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await accessService.CanAccessProjectAsync(
                currentUser.UserId, projectId, cancellationToken))
            return NotFound();
        return Ok(await svc.GetPreferencesAsync(currentUser.UserId, projectId));
    }

    /// <summary>Saves (insert or update) the authenticated user's dashboard preferences for the given project.</summary>
    [HttpPut]
    public async Task<IActionResult> Put(
        [FromBody] DashboardPreferenceDto dto,
        [FromQuery] int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await accessService.CanAccessProjectAsync(
                currentUser.UserId, projectId, cancellationToken))
            return NotFound();
        await svc.SavePreferencesAsync(currentUser.UserId, projectId, dto);
        return NoContent();
    }
}
