using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

/// <summary>
/// Manages per-user dashboard layout preferences for a given project.
/// </summary>
[ApiController]
[Route("api/dashboard-preferences")]
public class DashboardPreferenceController : ControllerBase
{
    private readonly IDashboardPreferenceService _svc;

    /// <summary>Initialises a new instance of <see cref="DashboardPreferenceController"/>.</summary>
    public DashboardPreferenceController(IDashboardPreferenceService svc) => _svc = svc;

    /// <summary>Returns the authenticated user's saved dashboard preferences for the given project.</summary>
    [HttpGet]
    public async Task<ActionResult<DashboardPreferenceDto>> Get([FromQuery] int projectId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();
        return Ok(await _svc.GetPreferencesAsync(userId, projectId));
    }

    /// <summary>Saves (insert or update) the authenticated user's dashboard preferences for the given project.</summary>
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] DashboardPreferenceDto dto, [FromQuery] int projectId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();
        await _svc.SavePreferencesAsync(userId, projectId, dto);
        return NoContent();
    }
}
