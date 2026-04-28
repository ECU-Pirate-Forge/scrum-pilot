using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>
/// Business-logic contract for managing <see cref="Project"/> entities.
/// </summary>
public interface IProjectService
{
    /// <summary>Returns all projects.</summary>
    Task<IEnumerable<Project>> GetAllProjectsAsync();

    /// <summary>Returns the project with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<Project?> GetByIdAsync(int id);

    /// <summary>Creates a new project and returns it with its database-assigned ID.</summary>
    Task<Project> CreateAsync(Project project);

    /// <summary>Updates an existing project and returns the saved entity.</summary>
    Task<Project> UpdateAsync(Project project);

    /// <summary>Deletes the project with the given <paramref name="id"/>.</summary>
    Task DeleteAsync(int id);
}
