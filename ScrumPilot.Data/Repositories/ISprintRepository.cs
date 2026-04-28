using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    /// <summary>
    /// Data-access contract for <see cref="Sprint"/> persistence.
    /// </summary>
    public interface ISprintRepository
    {
        /// <summary>Returns all sprints across every project, ordered by start date descending.</summary>
        Task<IEnumerable<Sprint>> GetAllSprintsAsync();

        /// <summary>Returns all sprints belonging to the given <paramref name="projectId"/>.</summary>
        Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId);

        /// <summary>Persists a new sprint and returns it with its database-assigned ID.</summary>
        Task<Sprint> CreateAsync(Sprint sprint);

        /// <summary>Saves all changes to an existing sprint and returns the updated entity.</summary>
        Task<Sprint> UpdateAsync(Sprint sprint);

        /// <summary>
        /// Deletes the sprint with the given <paramref name="id"/> and unassigns all its PBIs.
        /// </summary>
        Task DeleteAsync(int id);
    }
}
