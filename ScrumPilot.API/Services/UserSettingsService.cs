using Microsoft.AspNetCore.Identity;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public class UserSettingsService : IUserSettingsService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserSettingsService(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    public async Task<UserSettingsDto?> GetSettingsAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;

        return new UserSettingsDto
        {
            Email = user.Email,
            DiscordUsername = user.DiscordUsername,
            UiPreference = user.UiPreference,
            DefaultProjectId = user.DefaultProjectId
        };
    }

    public async Task<bool> UpdateSettingsAsync(string userId, UserSettingsDto dto)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return false;

        if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != user.Email)
        {
            user.Email = dto.Email;
            user.NormalizedEmail = dto.Email.ToUpperInvariant();
        }

        user.DiscordUsername = dto.DiscordUsername;
        user.UiPreference = dto.UiPreference;
        user.DefaultProjectId = dto.DefaultProjectId;

        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded;
    }

    public async Task<(bool Succeeded, IEnumerable<string> Errors)> ChangePasswordAsync(
        string userId, string currentPassword, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return (false, ["User not found."]);

        var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        return (result.Succeeded, result.Errors.Select(e => e.Description));
    }

    public Task<IEnumerable<UserSummaryDto>> GetAllUsersAsync()
    {
        var users = _userManager.Users
            .OrderBy(u => u.UserName)
            .Select(u => new UserSummaryDto { Id = u.Id, UserName = u.UserName ?? "" })
            .AsEnumerable();
        return Task.FromResult(users);
    }
}
