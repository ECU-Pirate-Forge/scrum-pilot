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
    public ScrumPilot.Data.Tenancy.TenantStorage? Storage { get; init; }
    public IServiceProvider? Services { get; init; }
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ControlPlaneContext _context;

    public UserSettingsService(
        UserManager<ApplicationUser> userManager,
        ControlPlaneContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public async Task<UserSettingsDto?> GetSettingsAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;
        var defaultOrg = user.DefaultOrganizationId;
        var defaultProject = user.DefaultProjectId;
        if (!defaultOrg.HasValue) defaultProject = null;
        if (Storage != null && defaultOrg.HasValue)
        {
            var membership = await _context.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(m =>
                m.UserId == userId && m.OrganizationId == defaultOrg && m.Organization!.DeletedAt == null
                && m.Organization.StorageState == "Active");
            if (membership is null) { defaultOrg = null; defaultProject = null; }
            else if (defaultProject.HasValue && !await ValidateTenantProject(userId, defaultOrg.Value, defaultProject.Value, CancellationToken.None))
                defaultProject = null;
        }

        return new UserSettingsDto
        {
            Email = user.Email,
            UiPreference = user.UiPreference,
            DefaultProjectId = defaultProject,
            DefaultOrganizationId = defaultOrg
        };
    }

    public async Task<UserSettingsUpdateResult> UpdateSettingsAsync(
        string userId,
        UserSettingsDto dto,
        CancellationToken cancellationToken = default)
    {
        OrganizationContext? selectedTenant = null;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? selectedWork = null;
        try
        {
            // Hold local project/grant read locks through the central commit. Membership is
            // re-read under the central serializable transaction, including its current role.
            Guid? validatedMembership = null;
            var projectExists = false;
            var explicitGrant = false;
            if (Storage != null && dto.DefaultProjectId.HasValue && dto.DefaultOrganizationId.HasValue)
            {
                validatedMembership = await _context.OrganizationMemberships.AsNoTracking()
                    .Where(m => m.OrganizationId == dto.DefaultOrganizationId && m.UserId == userId)
                    .Select(m => (Guid?)m.MembershipInstanceId).SingleOrDefaultAsync(cancellationToken);
                var organization = await _context.Organizations.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.OrganizationId == dto.DefaultOrganizationId, cancellationToken);
                if (organization == null || validatedMembership == null)
                    return UserSettingsUpdateResult.Validation("The selected default project is not available.");
                try { selectedTenant = Storage.Open(organization); }
                catch (ScrumPilot.Data.Tenancy.TenantUnavailableException)
                { return UserSettingsUpdateResult.Validation("The selected default project is not available."); }
                selectedWork = await selectedTenant.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                projectExists = await selectedTenant.Projects.AnyAsync(p => p.ProjectId == dto.DefaultProjectId
                    && p.OrganizationId == dto.DefaultOrganizationId, cancellationToken);
                explicitGrant = await selectedTenant.ProjectMemberships.AnyAsync(g => g.ProjectId == dto.DefaultProjectId
                    && g.UserId == userId && g.MembershipInstanceId == validatedMembership, cancellationToken);
            }
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
                        && membership.Organization.DeletedAt == null
                        && membership.Organization.StorageState == "Active"
                        && !membership.Organization.RestorePending,
                    cancellationToken);
            if (!hasOrganizationAccess)
            {
                return UserSettingsUpdateResult.Validation(
                    "The selected default organization is not available.");
            }

            if (dto.DefaultProjectId.HasValue)
            {
                var hasProjectAccess = projectExists && validatedMembership.HasValue
                        && await _context.OrganizationMemberships.AsNoTracking().AnyAsync(m =>
                            m.OrganizationId == dto.DefaultOrganizationId && m.UserId == userId
                            && m.MembershipInstanceId == validatedMembership
                            && (m.Role == OrganizationRole.Owner || explicitGrant), cancellationToken);
                if (!hasProjectAccess)
                {
                    return UserSettingsUpdateResult.Validation(
                        "The selected default project is not available.");
                }
            }

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
        finally
        {
            if (selectedWork != null) await selectedWork.DisposeAsync();
            if (selectedTenant != null) await selectedTenant.DisposeAsync();
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
        CancellationToken cancellationToken = default)
    {
        if (Services != null)
        {
            var members = await Services.GetRequiredService<IProjectAccessRepository>().GetMembersAsync(projectId, cancellationToken);
            var ids = members.Select(m => m.UserId).ToArray();
            return await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .OrderBy(u => u.UserName).Select(u => new UserSummaryDto { Id = u.Id, UserName = u.UserName ?? "" }).ToListAsync(cancellationToken);
        }
        throw new InvalidOperationException("The selected organization's project access service is required.");
    }

    private async Task<bool> ValidateTenantProject(string userId, int organizationId, int projectId, CancellationToken ct)
    {
        var org = await _context.Organizations.AsNoTracking().SingleOrDefaultAsync(x => x.OrganizationId == organizationId, ct);
        var membership = await _context.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.UserId == userId, ct);
        if (org is null || membership is null || org.DeletedAt != null || org.StorageState != "Active") return false;
        try
        {
            using var tenant = Storage!.Open(org);
            return await tenant.Projects.AnyAsync(p => p.ProjectId == projectId && p.OrganizationId == organizationId
                && (membership.Role == OrganizationRole.Owner || tenant.ProjectMemberships.Any(g =>
                    g.ProjectId == projectId && g.UserId == userId && g.MembershipInstanceId == membership.MembershipInstanceId)), ct);
        }
        catch (ScrumPilot.Data.Tenancy.TenantUnavailableException) { return false; }
    }

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
