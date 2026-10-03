using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public class UserSettingsService : IUserSettingsService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ScrumPilotContext _context;
    private readonly IOrganizationAccessService _accessService;

    public UserSettingsService(
        UserManager<ApplicationUser> userManager,
        ScrumPilotContext context,
        IOrganizationAccessService accessService)
    {
        _userManager = userManager;
        _context = context;
        _accessService = accessService;
    }

    public async Task<UserSettingsDto?> GetSettingsAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;

        return new UserSettingsDto
        {
            Email = user.Email,
            DiscordUsername = user.DiscordUsername,
            UiPreference = user.UiPreference,
            DefaultProjectId = user.DefaultProjectId,
            DefaultOrganizationId = user.DefaultOrganizationId
        };
    }

    public async Task<UserSettingsUpdateResult> UpdateSettingsAsync(
        string userId,
        UserSettingsDto dto,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return UserSettingsUpdateResult.NotFound;

        if (dto.Email is not null
            && !string.Equals(dto.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return UserSettingsUpdateResult.Validation(
                "Email changes require a confirmed-email workflow, which is not currently available.");
        }

        if (dto.DefaultProjectId.HasValue && !dto.DefaultOrganizationId.HasValue)
        {
            return UserSettingsUpdateResult.Validation(
                "A default organization is required when selecting a default project.");
        }

        if (dto.DefaultOrganizationId.HasValue
            && !await _accessService.IsOrganizationMemberAsync(
                userId,
                dto.DefaultOrganizationId.Value,
                cancellationToken))
        {
            return UserSettingsUpdateResult.Validation(
                "The selected default organization is not available.");
        }

        if (dto.DefaultProjectId.HasValue)
        {
            var projectOrganizationId =
                await _accessService.GetOrganizationIdForProjectAsync(
                    dto.DefaultProjectId.Value,
                    cancellationToken);
            if (projectOrganizationId != dto.DefaultOrganizationId)
            {
                return UserSettingsUpdateResult.Validation(
                    "The selected default project does not belong to the selected organization.");
            }

            if (!await _accessService.CanAccessProjectAsync(
                userId,
                dto.DefaultProjectId.Value,
                cancellationToken))
            {
                return UserSettingsUpdateResult.Validation(
                    "The selected default project is not available.");
            }
        }

        user.DiscordUsername = dto.DiscordUsername;
        user.UiPreference = dto.UiPreference;
        user.DefaultOrganizationId = dto.DefaultOrganizationId;
        user.DefaultProjectId = dto.DefaultProjectId;

        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded
            ? UserSettingsUpdateResult.Success
            : UserSettingsUpdateResult.Failure(result.Errors.Select(error => error.Description));
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

    public async Task<IEnumerable<UserSummaryDto>> GetProjectUsersAsync(
        int projectId,
        CancellationToken cancellationToken = default) =>
        await (
            from user in _context.Users.AsNoTracking()
            where _context.Projects.Any(project =>
                project.ProjectId == projectId
                && project.Organization != null
                && project.Organization.DeletedAt == null
                && (_context.OrganizationMemberships.Any(m =>
                        m.UserId == user.Id
                        && m.OrganizationId == project.OrganizationId
                        && m.Role == OrganizationRole.Owner)
                    || (_context.ProjectMemberships.Any(m =>
                            m.ProjectId == projectId && m.UserId == user.Id)
                        && _context.OrganizationMemberships.Any(m =>
                            m.UserId == user.Id
                            && m.OrganizationId == project.OrganizationId))))
            orderby user.UserName
            select new UserSummaryDto { Id = user.Id, UserName = user.UserName ?? "" })
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserSummaryDto>> SearchUsersAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim().ToUpperInvariant();
        if (normalized.Length < 2) return [];
        return await _context.Users.AsNoTracking()
            .Where(user =>
                (user.NormalizedUserName != null && user.NormalizedUserName.Contains(normalized))
                || (user.NormalizedEmail != null && user.NormalizedEmail.Contains(normalized)))
            .OrderBy(user => user.UserName)
            .Take(Math.Clamp(limit, 1, 20))
            .Select(user => new UserSummaryDto
            {
                Id = user.Id,
                UserName = user.UserName ?? "",
                Email = user.Email
            })
            .ToListAsync(cancellationToken);
    }
}
