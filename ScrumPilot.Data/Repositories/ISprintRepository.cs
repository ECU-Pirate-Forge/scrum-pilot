using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    /// <summary>
    /// Data-access contract for <see cref="Sprint"/> persistence.
    /// </summary>
    public interface ISprintRepository
    {
        /// <summary>Returns all sprints across every project, ordered by start date descending.</summary>
        /// <summary>Returns all sprints belonging to the given <paramref name="projectId"/>.</summary>
        Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId, CancellationToken cancellationToken = default);

        Task<Sprint?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>Persists a new sprint and returns it with its database-assigned ID.</summary>
        Task<Sprint> CreateAsync(Sprint sprint, CancellationToken cancellationToken = default);

        /// <summary>Saves all changes to an existing sprint and returns the updated entity.</summary>
        Task<Sprint> UpdateAsync(Sprint sprint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the sprint with the given <paramref name="id"/> and unassigns all its PBIs.
        /// </summary>
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
