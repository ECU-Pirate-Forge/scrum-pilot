using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    /// <summary>
    /// Business-logic contract for managing <see cref="Sprint"/> entities.
    /// </summary>
    public interface ISprintService
    {
        /// <summary>Returns all sprints across every project.</summary>
        Task<IEnumerable<Sprint>> GetAllSprintsAsync();

        /// <summary>Returns all sprints belonging to the given <paramref name="projectId"/>.</summary>
        Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId);

        /// <summary>Creates a new sprint and returns it with its database-assigned ID.</summary>
        Task<Sprint> CreateAsync(Sprint sprint);

        /// <summary>Updates an existing sprint and returns the saved entity.</summary>
        Task<Sprint> UpdateAsync(Sprint sprint);

        /// <summary>Deletes the sprint and unassigns all its PBIs.</summary>
        Task DeleteAsync(int id);
    }
}
