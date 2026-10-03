using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>
/// Business-logic contract for managing <see cref="Project"/> entities.
/// </summary>
public interface IProjectService
{
    Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default);

    Task<Project> GetAccessibleProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<Project> CreateAsync(
        string ownerUserId,
        int organizationId,
        CreateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task<Project> UpdateAsync(
        string ownerUserId,
        int projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string ownerUserId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectMemberAccessDto>> GetMembersAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task SetAccessAsync(
        string ownerUserId,
        int projectId,
        string userId,
        SetProjectAccessRequest request,
        CancellationToken cancellationToken = default);
}

public abstract class ProjectApplicationException(string message) : Exception(message);

public sealed class ProjectValidationException(string message)
    : ProjectApplicationException(message);

public sealed class ProjectForbiddenException(
    string message = "You do not have permission to perform this project operation.")
    : ProjectApplicationException(message);

public sealed class ProjectNotFoundException(
    string message = "The project was not found.")
    : ProjectApplicationException(message);

public sealed class ProjectConflictException(string message)
    : ProjectApplicationException(message);
