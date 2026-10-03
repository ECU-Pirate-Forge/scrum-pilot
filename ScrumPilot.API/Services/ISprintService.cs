using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    /// <summary>
    /// Business-logic contract for managing <see cref="Sprint"/> entities.
    /// </summary>
    public interface ISprintService
    {
        /// <summary>Returns all sprints across every project.</summary>
        /// <summary>Returns all sprints belonging to the given <paramref name="projectId"/>.</summary>
        Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId, CancellationToken cancellationToken = default);

        /// <summary>Creates a new sprint and returns it with its database-assigned ID.</summary>
        Task<Sprint> CreateAsync(Sprint sprint, CancellationToken cancellationToken = default);

        /// <summary>Updates an existing sprint and returns the saved entity.</summary>
        Task<Sprint> UpdateAsync(Sprint sprint, CancellationToken cancellationToken = default);

        /// <summary>Deletes the sprint and unassigns all its PBIs.</summary>
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
