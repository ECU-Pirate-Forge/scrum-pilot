using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/dashboard-preferences")]
public class DashboardPreferenceController : ControllerBase
{
    private readonly IDashboardPreferenceService _svc;

    public DashboardPreferenceController(IDashboardPreferenceService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<DashboardPreferenceDto>> Get()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();
        return Ok(await _svc.GetPreferencesAsync(userId));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] DashboardPreferenceDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();
        await _svc.SavePreferencesAsync(userId, dto);
        return NoContent();
    }
}
