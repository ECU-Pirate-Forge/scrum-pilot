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
    /// Validates and applies the supplied <paramref name="dto"/> to the user's profile.
    /// </summary>
    Task<UserSettingsUpdateResult> UpdateSettingsAsync(
        string userId,
        UserSettingsDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the user's password after verifying <paramref name="currentPassword"/>.
    /// Returns a success flag and any validation error messages.
    /// </summary>
    Task<(bool Succeeded, IEnumerable<string> Errors)> ChangePasswordAsync(string userId, string currentPassword, string newPassword);

    /// <summary>Returns users eligible for assignment in a project.</summary>
    Task<IEnumerable<UserSummaryDto>> GetProjectUsersAsync(int projectId, CancellationToken cancellationToken = default);

    /// <summary>Returns a bounded user search used only by global administrators.</summary>
    Task<IReadOnlyList<UserSummaryDto>> SearchUsersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record UserSettingsUpdateResult(
    bool Succeeded,
    bool UserFound,
    IReadOnlyList<string> Errors)
{
    public static UserSettingsUpdateResult Success { get; } = new(true, true, []);

    public static UserSettingsUpdateResult NotFound { get; } =
        new(false, false, ["User not found."]);

    public static UserSettingsUpdateResult Validation(string error) =>
        new(false, true, [error]);

    public static UserSettingsUpdateResult Failure(IEnumerable<string> errors) =>
        new(false, true, errors.ToArray());
}
