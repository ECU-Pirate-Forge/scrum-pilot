using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Authorization;

public sealed class OrganizationAccessService(ScrumPilotContext context)
    : IOrganizationAccessService
{
    public Task<bool> IsOrganizationMemberAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default) =>
        context.OrganizationMemberships
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.UserId == userId
                    && membership.OrganizationId == organizationId
                    && membership.Organization != null
                    && membership.Organization.DeletedAt == null,
                cancellationToken);

    public Task<bool> IsOrganizationOwnerAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default) =>
        context.OrganizationMemberships
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.UserId == userId
                    && membership.OrganizationId == organizationId
                    && membership.Role == OrganizationRole.Owner
                    && membership.Organization != null
                    && membership.Organization.DeletedAt == null,
                cancellationToken);

    public Task<bool> CanAccessProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        context.Projects
            .AsNoTracking()
            .AnyAsync(
                project =>
                    project.ProjectId == projectId
                    && project.Organization != null
                    && project.Organization.DeletedAt == null
                    && (context.OrganizationMemberships.Any(
                            membership =>
                                membership.OrganizationId == project.OrganizationId
                                && membership.UserId == userId
                                && membership.Role == OrganizationRole.Owner)
                        || context.ProjectMemberships.Any(
                            membership =>
                                membership.ProjectId == project.ProjectId
                                && membership.UserId == userId
                                && context.OrganizationMemberships.Any(
                                    organizationMembership =>
                                        organizationMembership.OrganizationId
                                            == project.OrganizationId
                                        && organizationMembership.UserId == userId))),
                cancellationToken);

    public Task<int?> GetOrganizationIdForProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default) =>
        context.Projects
            .AsNoTracking()
            .Where(project =>
                project.ProjectId == projectId
                && project.Organization != null
                && project.Organization.DeletedAt == null)
            .Select(project => (int?)project.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<int?> GetOrganizationIdForPbiAsync(
        int pbiId,
        CancellationToken cancellationToken = default) =>
        (from pbi in context.Stories.AsNoTracking()
         join project in context.Projects.AsNoTracking() on pbi.ProjectId equals project.ProjectId
         join organization in context.Organizations.AsNoTracking()
             on project.OrganizationId equals organization.OrganizationId
         where pbi.PbiId == pbiId && organization.DeletedAt == null
         select (int?)organization.OrganizationId)
        .SingleOrDefaultAsync(cancellationToken);

    public Task<int?> GetOrganizationIdForSprintAsync(
        int sprintId,
        CancellationToken cancellationToken = default) =>
        (from sprint in context.Sprints.AsNoTracking()
         join project in context.Projects.AsNoTracking() on sprint.ProjectId equals project.ProjectId
         join organization in context.Organizations.AsNoTracking()
             on project.OrganizationId equals organization.OrganizationId
         where sprint.SprintId == sprintId && organization.DeletedAt == null
         select (int?)organization.OrganizationId)
        .SingleOrDefaultAsync(cancellationToken);

    public Task<int?> GetOrganizationIdForEpicAsync(
        int epicId,
        CancellationToken cancellationToken = default) =>
        (from epic in context.Epics.AsNoTracking()
         join project in context.Projects.AsNoTracking() on epic.ProjectId equals project.ProjectId
         join organization in context.Organizations.AsNoTracking()
             on project.OrganizationId equals organization.OrganizationId
         where epic.EpicId == epicId && organization.DeletedAt == null
         select (int?)organization.OrganizationId)
        .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> SprintBelongsToProjectAsync(
        int sprintId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        ResourceBelongsToActiveProject(
                context.Sprints.AsNoTracking()
                    .Where(sprint => sprint.SprintId == sprintId)
                    .Select(sprint => sprint.ProjectId),
                projectId)
            .AnyAsync(cancellationToken);

    public Task<bool> EpicBelongsToProjectAsync(
        int epicId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        ResourceBelongsToActiveProject(
                context.Epics.AsNoTracking()
                    .Where(epic => epic.EpicId == epicId)
                    .Select(epic => epic.ProjectId),
                projectId)
            .AnyAsync(cancellationToken);

    public Task<bool> PbiBelongsToProjectAsync(
        int pbiId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        ResourceBelongsToActiveProject(
                context.Stories.AsNoTracking()
                    .Where(pbi => pbi.PbiId == pbiId)
                    .Select(pbi => pbi.ProjectId),
                projectId)
            .AnyAsync(cancellationToken);

    public Task<bool> UserCanBeAssignedToProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        CanAccessProjectAsync(userId, projectId, cancellationToken);

    private IQueryable<int> ResourceBelongsToActiveProject(
        IQueryable<int> resourceProjectIds,
        int projectId) =>
        from resourceProjectId in resourceProjectIds
        join project in context.Projects.AsNoTracking()
            on resourceProjectId equals project.ProjectId
        join organization in context.Organizations.AsNoTracking()
            on project.OrganizationId equals organization.OrganizationId
        where project.ProjectId == projectId && organization.DeletedAt == null
        select project.ProjectId;
}
