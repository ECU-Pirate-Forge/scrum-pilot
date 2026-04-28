using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    /// <summary>
    /// Data-access contract for <see cref="Epic"/> persistence.
    /// </summary>
    public interface IEpicRepository
    {
        /// <summary>Returns all epics across every project.</summary>
        Task<IEnumerable<Epic>> GetAllEpicsAsync();

        /// <summary>Returns all epics belonging to the given <paramref name="projectId"/>.</summary>
        Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId);

        /// <summary>Persists a new epic and returns it with its database-assigned ID.</summary>
        Task<Epic> CreateAsync(Epic epic);

        /// <summary>Saves all changes to an existing epic and returns the updated entity.</summary>
        Task<Epic> UpdateAsync(Epic epic);

        /// <summary>Deletes the epic with the given <paramref name="id"/>.</summary>
        Task DeleteAsync(int id);
    }
}
