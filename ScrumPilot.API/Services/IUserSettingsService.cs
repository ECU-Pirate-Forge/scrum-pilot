using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>
/// Business-logic contract for reading and updating user profile settings.
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// Returns the settings for the given <paramref name="userId"/>,
    /// or <c>null</c> if the user does not exist.
    /// </summary>
    Task<UserSettingsDto?> GetSettingsAsync(string userId);

    /// <summary>
    /// Applies the supplied <paramref name="dto"/> to the user's profile.
    /// Returns <c>true</c> on success.
    /// </summary>
    Task<bool> UpdateSettingsAsync(string userId, UserSettingsDto dto);

    /// <summary>
    /// Changes the user's password after verifying <paramref name="currentPassword"/>.
    /// Returns a success flag and any validation error messages.
    /// </summary>
    Task<(bool Succeeded, IEnumerable<string> Errors)> ChangePasswordAsync(string userId, string currentPassword, string newPassword);

    /// <summary>Returns a lightweight summary of every registered user.</summary>
    Task<IEnumerable<UserSummaryDto>> GetAllUsersAsync();
}
