using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public interface IUserSettingsService
{
    Task<UserSettingsDto?> GetSettingsAsync(string userId);
    Task<bool> UpdateSettingsAsync(string userId, UserSettingsDto dto);
    Task<(bool Succeeded, IEnumerable<string> Errors)> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
    Task<IEnumerable<UserSummaryDto>> GetAllUsersAsync();
}
