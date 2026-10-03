using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public sealed class ProjectAccessRepository(ScrumPilotContext context)
    : IProjectAccessRepository
{
    public async Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default) =>
        await context.Projects
            .AsNoTracking()
            .Where(project =>
                project.OrganizationId == organizationId
                && project.Organization != null
                && project.Organization.DeletedAt == null
                && (context.OrganizationMemberships.Any(membership =>
                        membership.OrganizationId == organizationId
                        && membership.UserId == userId
                        && membership.Role == OrganizationRole.Owner)
                    || (context.OrganizationMemberships.Any(membership =>
                            membership.OrganizationId == organizationId
                            && membership.UserId == userId)
                        && context.ProjectMemberships.Any(membership =>
                            membership.ProjectId == project.ProjectId
                            && membership.UserId == userId))))
            .OrderBy(project => project.ProjectName)
            .ToListAsync(cancellationToken);

    public Task<Project?> GetAccessibleProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        context.Projects
            .AsNoTracking()
            .Where(project =>
                project.ProjectId == projectId
                && project.Organization != null
                && project.Organization.DeletedAt == null
                && (context.OrganizationMemberships.Any(membership =>
                        membership.OrganizationId == project.OrganizationId
                        && membership.UserId == userId
                        && membership.Role == OrganizationRole.Owner)
                    || (context.OrganizationMemberships.Any(membership =>
                            membership.OrganizationId == project.OrganizationId
                            && membership.UserId == userId)
                        && context.ProjectMemberships.Any(membership =>
                            membership.ProjectId == project.ProjectId
                            && membership.UserId == userId))))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<Project?> GetForUpdateAsync(
        int projectId,
        CancellationToken cancellationToken = default) =>
        context.Projects
            .Where(project =>
                project.ProjectId == projectId
                && project.Organization != null
                && project.Organization.DeletedAt == null)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Project> CreateAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        context.Projects.Add(project);
        await context.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task<Project> SaveAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        await context.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task<bool> DeleteAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await context.Projects
            .SingleOrDefaultAsync(project => project.ProjectId == projectId, cancellationToken);
        if (project is null)
        {
            return false;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var usersWithProjectDefault = await context.Users
            .Where(user => user.DefaultProjectId == projectId)
            .ToListAsync(cancellationToken);
        foreach (var user in usersWithProjectDefault)
        {
            user.DefaultProjectId = null;
        }

        var preferences = await context.UserDashboardPreferences
            .Where(preference => preference.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        var memberships = await context.ProjectMemberships
            .Where(membership => membership.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        context.UserDashboardPreferences.RemoveRange(preferences);
        context.ProjectMemberships.RemoveRange(memberships);
        context.Projects.Remove(project);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ProjectMemberAccessDto>> GetMembersAsync(
        int projectId,
        CancellationToken cancellationToken = default) =>
        await (
            from project in context.Projects.AsNoTracking()
            join organization in context.Organizations.AsNoTracking()
                on project.OrganizationId equals organization.OrganizationId
            join membership in context.OrganizationMemberships.AsNoTracking()
                on organization.OrganizationId equals membership.OrganizationId
            where project.ProjectId == projectId
                && organization.DeletedAt == null
                && (membership.Role == OrganizationRole.Owner
                    || context.ProjectMemberships.Any(projectMembership =>
                        projectMembership.ProjectId == projectId
                        && projectMembership.UserId == membership.UserId))
            orderby membership.UserId
            select new ProjectMemberAccessDto(
                membership.UserId,
                membership.Role,
                context.ProjectMemberships.Any(projectMembership =>
                    projectMembership.ProjectId == projectId
                    && projectMembership.UserId == membership.UserId)))
        .ToListAsync(cancellationToken);

    public Task<OrganizationRole?> GetOrganizationRoleAsync(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default) =>
        context.OrganizationMemberships
            .AsNoTracking()
            .Where(membership =>
                membership.OrganizationId == organizationId
                && membership.UserId == userId
                && membership.Organization != null
                && membership.Organization.DeletedAt == null)
            .Select(membership => (OrganizationRole?)membership.Role)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task SetAccessAsync(
        int projectId,
        string userId,
        bool hasAccess,
        string grantedByUserId,
        DateTime grantedAt,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var membership = await context.ProjectMemberships
            .SingleOrDefaultAsync(
                candidate => candidate.ProjectId == projectId && candidate.UserId == userId,
                cancellationToken);

        if (hasAccess)
        {
            if (membership is null)
            {
                context.ProjectMemberships.Add(new ProjectMembership
                {
                    ProjectId = projectId,
                    UserId = userId,
                    GrantedByUserId = grantedByUserId,
                    GrantedAt = grantedAt
                });
            }
        }
        else
        {
            if (membership is not null)
            {
                context.ProjectMemberships.Remove(membership);
            }

            var user = await context.Users.SingleAsync(
                candidate => candidate.Id == userId,
                cancellationToken);
            if (user.DefaultProjectId == projectId)
            {
                user.DefaultProjectId = null;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
