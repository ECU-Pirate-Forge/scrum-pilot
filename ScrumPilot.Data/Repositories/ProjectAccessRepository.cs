using System.Data;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public sealed class ProjectAccessRepository(ScrumPilotContext context)
    : IProjectAccessRepository
{
    public async Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        await context.Projects
            .AsNoTracking()
            .Where(project =>
                project.Organization != null
                && project.Organization.DeletedAt == null
                && context.OrganizationMemberships.Any(membership =>
                    membership.OrganizationId == project.OrganizationId
                    && membership.UserId == userId
                    && (membership.Role == OrganizationRole.Owner
                        || context.ProjectMemberships.Any(projectMembership =>
                            projectMembership.ProjectId == project.ProjectId
                            && projectMembership.UserId == userId))))
            .OrderBy(project => project.ProjectName)
            .ThenBy(project => project.ProjectId)
            .ToListAsync(cancellationToken);

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

    public async Task<ProjectAccessMutationResult> SetAccessAsync(
        int projectId,
        string userId,
        bool hasAccess,
        string grantedByUserId,
        DateTime grantedAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var organizationId = await context.Projects
                .Where(project =>
                    project.ProjectId == projectId
                    && project.Organization != null
                    && project.Organization.DeletedAt == null)
                .Select(project => (int?)project.OrganizationId)
                .SingleOrDefaultAsync(cancellationToken);
            if (organizationId is null)
            {
                return ProjectAccessMutationResult.ProjectNotFound;
            }

            var targetRole = await context.OrganizationMemberships
                .Where(membership =>
                    membership.OrganizationId == organizationId.Value
                    && membership.UserId == userId)
                .Select(membership => (OrganizationRole?)membership.Role)
                .SingleOrDefaultAsync(cancellationToken);
            if (targetRole is null)
            {
                await RemoveProjectAccessAndDefaultAsync(
                    projectId,
                    userId,
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ProjectAccessMutationResult.TargetNotMember;
            }
            if (targetRole == OrganizationRole.Owner)
            {
                return ProjectAccessMutationResult.TargetIsOwner;
            }

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

                await ClearMatchingDefaultAsync(projectId, userId, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ProjectAccessMutationResult.Success;
        }
        catch (Exception exception) when (
            hasAccess
            && ProjectAccessRepositoryExceptionClassifier
                .IsProjectMembershipUniqueViolation(exception))
        {
            context.ChangeTracker.Clear();
            if (await HasValidProjectAccessAsync(projectId, userId, cancellationToken))
            {
                return ProjectAccessMutationResult.Success;
            }

            throw new ProjectAccessConcurrencyException(
                "Project access changed during the request.",
                exception);
        }
        catch (Exception exception) when (
            ProjectAccessRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception))
        {
            throw new ProjectAccessConcurrencyException(
                "Project access changed during the request.",
                exception);
        }
    }

    private async Task RemoveProjectAccessAndDefaultAsync(
        int projectId,
        string userId,
        CancellationToken cancellationToken)
    {
        var membership = await context.ProjectMemberships.SingleOrDefaultAsync(
            candidate => candidate.ProjectId == projectId && candidate.UserId == userId,
            cancellationToken);
        if (membership is not null)
        {
            context.ProjectMemberships.Remove(membership);
        }
        await ClearMatchingDefaultAsync(projectId, userId, cancellationToken);
    }

    private async Task ClearMatchingDefaultAsync(
        int projectId,
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await context.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken);
        if (user?.DefaultProjectId == projectId)
        {
            user.DefaultProjectId = null;
        }
    }

    private Task<bool> HasValidProjectAccessAsync(
        int projectId,
        string userId,
        CancellationToken cancellationToken) =>
        context.ProjectMemberships
            .AsNoTracking()
            .AnyAsync(
                projectMembership =>
                    projectMembership.ProjectId == projectId
                    && projectMembership.UserId == userId
                    && context.Projects.Any(project =>
                        project.ProjectId == projectId
                        && project.Organization != null
                        && project.Organization.DeletedAt == null
                        && context.OrganizationMemberships.Any(organizationMembership =>
                            organizationMembership.OrganizationId == project.OrganizationId
                            && organizationMembership.UserId == userId
                            && organizationMembership.Role != OrganizationRole.Owner)),
                cancellationToken);
}
