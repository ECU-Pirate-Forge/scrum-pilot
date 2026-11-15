using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Authorization;

public sealed class TenantOrganizationAccessService(
    ControlPlaneContext control, IServiceProvider services, TenantSelection selection) : IOrganizationAccessService
{
    private OrganizationContext Tenant => services.GetRequiredService<OrganizationContext>();
    public Task<bool> IsOrganizationMemberAsync(string userId, int organizationId, CancellationToken cancellationToken = default) =>
        control.OrganizationMemberships.AsNoTracking().AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId
            && m.Organization!.DeletedAt == null && m.Organization.StorageState == "Active", cancellationToken);
    public Task<bool> IsOrganizationOwnerAsync(string userId, int organizationId, CancellationToken cancellationToken = default) =>
        control.OrganizationMemberships.AsNoTracking().AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId
            && m.Role == OrganizationRole.Owner && m.Organization!.DeletedAt == null, cancellationToken);
    public async Task<bool> CanAccessProjectAsync(string userId, int projectId, CancellationToken cancellationToken = default) =>
        selection.OrganizationId is int organizationId
        && await IsOrganizationMemberAsync(userId, organizationId, cancellationToken)
        && await services.GetRequiredService<IProjectAccessRepository>().GetAccessibleProjectAsync(userId, projectId, cancellationToken) != null;
    public Task<int?> GetOrganizationIdForProjectAsync(int projectId, CancellationToken cancellationToken = default) =>
        Tenant.Projects.AsNoTracking().Where(x => x.ProjectId == projectId && x.OrganizationId == selection.OrganizationId)
            .Select(x => (int?)x.OrganizationId).SingleOrDefaultAsync(cancellationToken);
    private async Task<int?> ForProject(Task<int?> project, CancellationToken ct) =>
        (await project) is int id ? await GetOrganizationIdForProjectAsync(id, ct) : null;
    public Task<int?> GetOrganizationIdForPbiAsync(int pbiId, CancellationToken cancellationToken = default) =>
        ForProject(Tenant.Stories.Where(x => x.PbiId == pbiId).Select(x => (int?)x.ProjectId).SingleOrDefaultAsync(cancellationToken), cancellationToken);
    public Task<int?> GetOrganizationIdForSprintAsync(int sprintId, CancellationToken cancellationToken = default) =>
        ForProject(Tenant.Sprints.Where(x => x.SprintId == sprintId).Select(x => (int?)x.ProjectId).SingleOrDefaultAsync(cancellationToken), cancellationToken);
    public Task<int?> GetOrganizationIdForEpicAsync(int epicId, CancellationToken cancellationToken = default) =>
        ForProject(Tenant.Epics.Where(x => x.EpicId == epicId).Select(x => (int?)x.ProjectId).SingleOrDefaultAsync(cancellationToken), cancellationToken);
    public Task<bool> SprintBelongsToProjectAsync(int sprintId, int projectId, CancellationToken cancellationToken = default) =>
        Tenant.Sprints.AnyAsync(x => x.SprintId == sprintId && x.ProjectId == projectId, cancellationToken);
    public Task<bool> EpicBelongsToProjectAsync(int epicId, int projectId, CancellationToken cancellationToken = default) =>
        Tenant.Epics.AnyAsync(x => x.EpicId == epicId && x.ProjectId == projectId, cancellationToken);
    public Task<bool> PbiBelongsToProjectAsync(int pbiId, int projectId, CancellationToken cancellationToken = default) =>
        Tenant.Stories.AnyAsync(x => x.PbiId == pbiId && x.ProjectId == projectId, cancellationToken);
    public Task<bool> UserCanBeAssignedToProjectAsync(string userId, int projectId, CancellationToken cancellationToken = default) =>
        CanAccessProjectAsync(userId, projectId, cancellationToken);
}
