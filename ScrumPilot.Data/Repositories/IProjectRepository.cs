using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

/// <summary>
/// Data-access contract for <see cref="Project"/> persistence.
/// </summary>
public interface IProjectRepository
{
    /// <summary>Returns all projects.</summary>
    Task<IEnumerable<Project>> GetAllProjectsAsync();

    /// <summary>Returns the project with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
    Task<Project?> GetByIdAsync(int id);

    /// <summary>Persists a new project and returns it with its database-assigned ID.</summary>
    Task<Project> AddAsync(Project project);

    /// <summary>Saves all changes to an existing project and returns the updated entity.</summary>
    Task<Project> UpdateAsync(Project project);

    /// <summary>Deletes the project with the given <paramref name="id"/>.</summary>
    Task DeleteAsync(int id);
}
