using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public class UserSettingsService : IUserSettingsService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ScrumPilotContext _context;

    public UserSettingsService(
        UserManager<ApplicationUser> userManager,
        ScrumPilotContext context)
    {
        _userManager = userManager;
        _context = context;
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
        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var user = await _context.Users.SingleOrDefaultAsync(
                candidate => candidate.Id == userId,
                cancellationToken);
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

            var hasOrganizationAccess = !dto.DefaultOrganizationId.HasValue
                || await _context.OrganizationMemberships.AnyAsync(
                    membership =>
                        membership.UserId == userId
                        && membership.OrganizationId == dto.DefaultOrganizationId.Value
                        && membership.Organization != null
                        && membership.Organization.DeletedAt == null,
                    cancellationToken);
            if (!hasOrganizationAccess)
            {
                return UserSettingsUpdateResult.Validation(
                    "The selected default organization is not available.");
            }

            if (dto.DefaultProjectId.HasValue)
            {
                var hasProjectAccess = await _context.Projects.AnyAsync(
                    project =>
                        project.ProjectId == dto.DefaultProjectId.Value
                        && project.OrganizationId == dto.DefaultOrganizationId!.Value
                        && project.Organization != null
                        && project.Organization.DeletedAt == null
                        && _context.OrganizationMemberships.Any(membership =>
                            membership.UserId == userId
                            && membership.OrganizationId == project.OrganizationId
                            && (membership.Role == OrganizationRole.Owner
                                || _context.ProjectMemberships.Any(projectMembership =>
                                    projectMembership.ProjectId == project.ProjectId
                                    && projectMembership.UserId == userId))),
                    cancellationToken);
                if (!hasProjectAccess)
                {
                    return UserSettingsUpdateResult.Validation(
                        "The selected default project is not available.");
                }
            }

            user.DiscordUsername = dto.DiscordUsername;
            user.UiPreference = dto.UiPreference;
            user.DefaultOrganizationId = dto.DefaultOrganizationId;
            user.DefaultProjectId = dto.DefaultProjectId;

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return UserSettingsUpdateResult.Success;
        }
        catch (Exception exception) when (
            TransactionConcurrencyExceptionClassifier
                .IsConcurrencyConflict(exception))
        {
            return UserSettingsUpdateResult.Conflict(
                "Settings changed concurrently. Please reload and try again.");
        }
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
