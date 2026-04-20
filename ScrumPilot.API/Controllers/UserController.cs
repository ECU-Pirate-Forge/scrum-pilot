using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using System.Security.Claims;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserSettingsService _service;

    public UserController(IUserSettingsService service) => _service = service;

    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var dto = await _service.GetSettingsAsync(userId);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UserSettingsDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var success = await _service.UpdateSettingsAsync(userId, dto);
        return success ? NoContent() : BadRequest("Failed to update settings.");
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var (succeeded, errors) = await _service.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        return succeeded ? NoContent() : BadRequest(new { errors });
    }
}
