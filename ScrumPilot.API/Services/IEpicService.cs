using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    /// <summary>
    /// Business-logic contract for managing <see cref="Epic"/> entities.
    /// </summary>
    public interface IEpicService
    {
        /// <summary>Returns all epics across every project.</summary>
        Task<IEnumerable<Epic>> GetAllEpicsAsync();

        /// <summary>Returns all epics belonging to the given <paramref name="projectId"/>.</summary>
        Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId);

        /// <summary>Creates a new epic and returns it with its database-assigned ID.</summary>
        Task<Epic> CreateAsync(Epic epic);

        /// <summary>Updates an existing epic and returns the saved entity.</summary>
        Task<Epic> UpdateAsync(Epic epic);

        /// <summary>Deletes the epic with the given <paramref name="id"/>.</summary>
        Task DeleteAsync(int id);
    }
}
