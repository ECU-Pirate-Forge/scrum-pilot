using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

/// <summary>
/// Manages user profile settings and password changes.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class UserController(
    IUserSettingsService service,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService) : ControllerBase
{
    /// <summary>Returns the authenticated user's profile settings.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<UserSettingsDto>> GetSettings()
    {
        var dto = await service.GetSettingsAsync(currentUser.UserId);
        return dto is null ? Unauthorized() : Ok(dto);
    }

    /// <summary>Updates the authenticated user's profile settings.</summary>
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UserSettingsDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto.DefaultProjectId.HasValue
            && !await accessService.CanAccessProjectAsync(
                currentUser.UserId, dto.DefaultProjectId.Value, cancellationToken))
            return NotFound();
        var success = await service.UpdateSettingsAsync(currentUser.UserId, dto);
        return success ? NoContent() : BadRequest("Failed to update settings.");
    }

    /// <summary>Returns a lightweight summary of every registered user for assignment dropdowns.</summary>
    [HttpGet("all")]
    public async Task<ActionResult<IEnumerable<UserSummaryDto>>> GetAllUsers(
        [FromQuery] int projectId,
        CancellationToken cancellationToken)
    {
        if (!await accessService.CanAccessProjectAsync(
                currentUser.UserId, projectId, cancellationToken))
            return NotFound();
        var users = await service.GetProjectUsersAsync(projectId, cancellationToken);
        return Ok(users);
    }

    /// <summary>Searches users eligible to become the initial owner of a new organization.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("admin-search")]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> SearchUsers(
        [FromQuery] string query,
        CancellationToken cancellationToken)
    {
        var users = await service.SearchUsersAsync(query, 20, cancellationToken);
        return Ok(users);
    }

    /// <summary>Changes the authenticated user's password after verifying the current one.</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var (succeeded, errors) = await service.ChangePasswordAsync(
            currentUser.UserId, request.CurrentPassword, request.NewPassword);
        return succeeded ? NoContent() : BadRequest(new { errors });
    }
}
