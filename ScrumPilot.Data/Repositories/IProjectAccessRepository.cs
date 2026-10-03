using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public interface IProjectAccessRepository
{
    Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default);

    Task<Project?> GetAccessibleProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<Project?> GetForUpdateAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<Project> CreateAsync(
        Project project,
        CancellationToken cancellationToken = default);

    Task<Project> SaveAsync(
        Project project,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectMemberAccessDto>> GetMembersAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectAccessMutationResult> SetAccessAsync(
        int projectId,
        string userId,
        bool hasAccess,
        string grantedByUserId,
        DateTime grantedAt,
        CancellationToken cancellationToken = default);
}

public enum ProjectAccessMutationResult
{
    Success,
    ProjectNotFound,
    TargetNotMember,
    TargetIsOwner
}
