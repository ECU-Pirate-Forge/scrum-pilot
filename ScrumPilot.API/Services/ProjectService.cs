using ScrumPilot.Data.Repositories;
using ScrumPilot.API.Authorization;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>
/// Implements <see cref="IProjectService"/> by delegating to <see cref="IProjectRepository"/>.
/// </summary>
public sealed class ProjectService(
    IProjectAccessRepository repository,
    IOrganizationAccessService accessService,
    TimeProvider timeProvider) : IProjectService
{
    private const int MaxNameLength = 200;

    public async Task<IReadOnlyList<Project>> GetAccessibleProjectsAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        if (!await accessService.IsOrganizationMemberAsync(
                userId,
                organizationId,
                cancellationToken))
        {
            throw new ProjectNotFoundException();
        }

        return await repository.GetAccessibleProjectsAsync(
            userId,
            organizationId,
            cancellationToken);
    }

    public async Task<Project> GetAccessibleProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await accessService.CanAccessProjectAsync(userId, projectId, cancellationToken))
        {
            throw new ProjectNotFoundException();
        }

        return await repository.GetAccessibleProjectAsync(userId, projectId, cancellationToken)
            ?? throw new ProjectNotFoundException();
    }

    public async Task<Project> CreateAsync(
        string ownerUserId,
        int organizationId,
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireOrganizationOwnerAsync(ownerUserId, organizationId, cancellationToken);
        var name = ValidateName(request.ProjectName);
        return await repository.CreateAsync(
            new Project
            {
                OrganizationId = organizationId,
                ProjectName = name,
                Description = request.Description
            },
            cancellationToken);
    }

    public async Task<Project> UpdateAsync(
        string ownerUserId,
        int projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireProjectOwnerAsync(ownerUserId, projectId, cancellationToken);
        var name = ValidateName(request.ProjectName);
        var project = await repository.GetForUpdateAsync(projectId, cancellationToken)
            ?? throw new ProjectNotFoundException();
        project.ProjectName = name;
        project.Description = request.Description;
        return await repository.SaveAsync(project, cancellationToken);
    }

    public async Task DeleteAsync(
        string ownerUserId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        await RequireProjectOwnerAsync(ownerUserId, projectId, cancellationToken);
        if (!await repository.DeleteAsync(projectId, cancellationToken))
        {
            throw new ProjectNotFoundException();
        }
    }

    public async Task<IReadOnlyList<ProjectMemberAccessDto>> GetMembersAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await accessService.CanAccessProjectAsync(userId, projectId, cancellationToken))
        {
            throw new ProjectNotFoundException();
        }

        return await repository.GetMembersAsync(projectId, cancellationToken);
    }

    public async Task SetAccessAsync(
        string ownerUserId,
        int projectId,
        string userId,
        SetProjectAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var organizationId = await RequireProjectOwnerAsync(
            ownerUserId,
            projectId,
            cancellationToken);
        var targetRole = await repository.GetOrganizationRoleAsync(
            organizationId,
            userId,
            cancellationToken);
        if (targetRole is null)
        {
            throw new ProjectValidationException(
                "Project access can only be changed for an active member of this organization.");
        }
        if (targetRole == OrganizationRole.Owner)
        {
            throw new ProjectConflictException(
                "Organization owners have implicit access and cannot have explicit project access.");
        }

        await repository.SetAccessAsync(
            projectId,
            userId,
            request.HasAccess,
            ownerUserId,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
    }

    private async Task RequireOrganizationOwnerAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken)
    {
        if (await accessService.IsOrganizationOwnerAsync(userId, organizationId, cancellationToken))
        {
            return;
        }

        if (await accessService.IsOrganizationMemberAsync(userId, organizationId, cancellationToken))
        {
            throw new ProjectForbiddenException(
                "Only an organization owner can perform this operation.");
        }

        throw new ProjectNotFoundException();
    }

    private async Task<int> RequireProjectOwnerAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken)
    {
        if (!await accessService.CanAccessProjectAsync(userId, projectId, cancellationToken))
        {
            throw new ProjectNotFoundException();
        }

        var organizationId = await accessService.GetOrganizationIdForProjectAsync(
            projectId,
            cancellationToken) ?? throw new ProjectNotFoundException();
        if (!await accessService.IsOrganizationOwnerAsync(
                userId,
                organizationId,
                cancellationToken))
        {
            throw new ProjectForbiddenException(
                "Only an organization owner can perform this operation.");
        }

        return organizationId;
    }

    private static string ValidateName(string? suppliedName)
    {
        var name = suppliedName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new ProjectValidationException("Project name is required.");
        }
        if (name.Length > MaxNameLength)
        {
            throw new ProjectValidationException(
                $"Project name cannot exceed {MaxNameLength} characters.");
        }
        return name;
    }
}
