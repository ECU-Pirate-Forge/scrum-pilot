namespace ScrumPilot.API.Authorization;

public interface IOrganizationAccessService
{
    Task<bool> IsOrganizationMemberAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> IsOrganizationOwnerAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> CanAccessProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<int?> GetOrganizationIdForProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<int?> GetOrganizationIdForPbiAsync(
        int pbiId,
        CancellationToken cancellationToken = default);

    Task<int?> GetOrganizationIdForSprintAsync(
        int sprintId,
        CancellationToken cancellationToken = default);

    Task<int?> GetOrganizationIdForEpicAsync(
        int epicId,
        CancellationToken cancellationToken = default);

    Task<bool> SprintBelongsToProjectAsync(
        int sprintId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<bool> EpicBelongsToProjectAsync(
        int epicId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<bool> PbiBelongsToProjectAsync(
        int pbiId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<bool> UserCanBeAssignedToProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default);
}
