using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public sealed class TenantProjectAccessRepository(
    OrganizationContext tenant, ControlPlaneContext control, TenantSelection selection,
    IEnumerable<IProjectPlanningRetirement>? retirements = null) : IProjectAccessRepository
{
    private int OrganizationId => selection.OrganizationId ?? throw new TenantUnavailableException();
    private Task<OrganizationMembership?> Membership(string userId, CancellationToken ct) =>
        control.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == OrganizationId && x.UserId == userId
            && x.Organization!.DeletedAt == null && x.Organization.StorageState == "Active", ct);

    public async Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var membership = await Membership(userId, cancellationToken);
        if (membership is null) return [];
        return await tenant.Projects.AsNoTracking()
            .Where(p => p.OrganizationId == OrganizationId &&
                (membership.Role == OrganizationRole.Owner || tenant.ProjectMemberships.Any(g =>
                    g.ProjectId == p.ProjectId && g.UserId == userId && g.MembershipInstanceId == membership.MembershipInstanceId)))
            .OrderBy(p => p.ProjectName).ToListAsync(cancellationToken);
    }
    public Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(string userId, int organizationId, CancellationToken cancellationToken = default) =>
        organizationId == OrganizationId ? GetAccessibleProjectsAsync(userId, cancellationToken)
            : Task.FromResult<IReadOnlyList<Project>>([]);
    public async Task<Project?> GetAccessibleProjectAsync(string userId, int projectId, CancellationToken cancellationToken = default)
    {
        var membership = await Membership(userId, cancellationToken);
        if (membership is null) return null;
        return await tenant.Projects.AsNoTracking().SingleOrDefaultAsync(p =>
            p.ProjectId == projectId && p.OrganizationId == OrganizationId
            && (membership.Role == OrganizationRole.Owner || tenant.ProjectMemberships.Any(g =>
                g.ProjectId == projectId && g.UserId == userId && g.MembershipInstanceId == membership.MembershipInstanceId)), cancellationToken);
    }
    public Task<Project?> GetForUpdateAsync(int projectId, CancellationToken cancellationToken = default) =>
        tenant.Projects.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.OrganizationId == OrganizationId, cancellationToken);
    public async Task<Project> CreateAsync(Project project, CancellationToken cancellationToken = default)
    {
        if (project.OrganizationId != OrganizationId) throw new TenantUnavailableException();
        tenant.Projects.Add(project); await tenant.SaveChangesAsync(cancellationToken); return project;
    }
    public async Task<Project> SaveAsync(Project project, CancellationToken cancellationToken = default)
    { await tenant.SaveChangesAsync(cancellationToken); return project; }
    public async Task<bool> DeleteAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var project = await GetForUpdateAsync(projectId, cancellationToken);
        if (project is null) return false;
        // Central-first cleanup is safe if a later local write fails.
        await control.Users.Where(x => x.DefaultOrganizationId == OrganizationId && x.DefaultProjectId == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultProjectId, (int?)null), cancellationToken);
        await using var transaction = await tenant.Database.BeginTransactionAsync(cancellationToken);
        await tenant.AcquirePlanningPolicyLockAsync(projectId, cancellationToken);
        foreach (var retirement in retirements ?? [])
            await retirement.RetireProjectAsync(OrganizationId, projectId, cancellationToken);
        await tenant.PurgeProjectPlanningAsync(projectId, cancellationToken);
        // SQL work FKs intentionally do not cascade from Project to Stories.
        // Remove work only after the scoped history purge, in the same transaction.
        await tenant.Stories.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        foreach (var entry in tenant.ChangeTracker.Entries<ProductBacklogItem>()
                     .Where(x => x.Entity.ProjectId == projectId).ToList())
            entry.State = EntityState.Detached;
        tenant.Projects.Remove(project);
        await tenant.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    public async Task<IReadOnlyList<ProjectMemberAccessDto>> GetMembersAsync(
        int projectId, CancellationToken cancellationToken = default, bool includeUngranted = false)
    {
        if (await GetForUpdateAsync(projectId, cancellationToken) is null) return [];
        var memberships = await control.OrganizationMemberships.AsNoTracking()
            .Where(x => x.OrganizationId == OrganizationId)
            .Select(x => new
            {
                x.UserId,
                x.Role,
                x.MembershipInstanceId,
                UserName = control.Users.Where(user => user.Id == x.UserId).Select(user => user.UserName).FirstOrDefault()
            }).ToListAsync(cancellationToken);
        var grants = await tenant.ProjectMemberships.AsNoTracking().Where(x => x.ProjectId == projectId).ToListAsync(cancellationToken);
        var currentGrants = grants.Select(g => (g.UserId, g.MembershipInstanceId)).ToHashSet();
        return memberships.Select(m => new ProjectMemberAccessDto(m.UserId, m.Role,
                currentGrants.Contains((m.UserId, m.MembershipInstanceId)), m.UserName))
            .Where(m => includeUngranted || m.OrganizationRole == OrganizationRole.Owner || m.HasExplicitAccess)
            .ToList();
    }
    public async Task<ProjectAccessMutationResult> SetAccessAsync(int projectId, string userId, bool hasAccess, string grantedByUserId, DateTime grantedAt, CancellationToken cancellationToken = default)
    {
        if (await GetForUpdateAsync(projectId, cancellationToken) is null) return ProjectAccessMutationResult.ProjectNotFound;
        var member = await Membership(userId, cancellationToken);
        if (member is null) return ProjectAccessMutationResult.TargetNotMember;
        if (member.Role == OrganizationRole.Owner) return ProjectAccessMutationResult.TargetIsOwner;
        var grant = await tenant.ProjectMemberships.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.UserId == userId, cancellationToken);
        if (hasAccess)
        {
            if (grant is null)
            { grant = new() { ProjectId = projectId, UserId = userId, GrantedByUserId = grantedByUserId }; tenant.ProjectMemberships.Add(grant); }
            grant.MembershipInstanceId = member.MembershipInstanceId;
            grant.GrantedAt = grantedAt; grant.GrantedByUserId = grantedByUserId;
        }
        else
        {
            await control.Users.Where(x => x.Id == userId && x.DefaultOrganizationId == OrganizationId && x.DefaultProjectId == projectId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultProjectId, (int?)null), cancellationToken);
            if (grant != null) tenant.ProjectMemberships.Remove(grant);
        }
        await tenant.SaveChangesAsync(cancellationToken);
        return ProjectAccessMutationResult.Success;
    }
}
