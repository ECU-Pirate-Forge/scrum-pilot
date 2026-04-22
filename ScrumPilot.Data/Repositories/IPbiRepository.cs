using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    /// <summary>
    /// Data-access contract for <see cref="ProductBacklogItem"/> persistence.
    /// </summary>
    public interface IPbiRepository
    {
        /// <summary>Returns all PBIs regardless of draft status, ordered by creation date descending.</summary>
        Task<IEnumerable<ProductBacklogItem>> GetAllPbisAsync();

        /// <summary>Returns the PBI with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
        Task<ProductBacklogItem?> GetByIdAsync(int id);

        /// <summary>Returns all non-draft PBIs, ordered by creation date descending.</summary>
        Task<IEnumerable<ProductBacklogItem>> GetNonDraftPbisAsync();

        /// <summary>Returns all draft PBIs, ordered by creation date descending.</summary>
        Task<IEnumerable<ProductBacklogItem>> GetDraftPbisAsync();

        /// <summary>
        /// Returns non-draft PBIs matching any combination of sprint, epic, and project filters.
        /// A <paramref name="sprintId"/> of <c>-1</c> returns PBIs with no sprint assigned.
        /// </summary>
        Task<IEnumerable<ProductBacklogItem>> GetFilteredPbisAsync(int? sprintId, int? epicId, int? projectId = null);

        /// <summary>Returns all PBIs with the given <paramref name="status"/>.</summary>
        Task<IEnumerable<ProductBacklogItem>> GetByStatusAsync(PbiStatus status);

        /// <summary>Persists a new PBI and returns it with its database-assigned ID.</summary>
        Task<ProductBacklogItem> AddAsync(ProductBacklogItem story);

        /// <summary>Saves all changes to an existing PBI and returns the updated entity.</summary>
        Task<ProductBacklogItem> UpdateAsync(ProductBacklogItem story);

        /// <summary>
        /// Permanently deletes the PBI with the given <paramref name="id"/>.
        /// Returns <c>true</c> if the record was found and removed; <c>false</c> if it did not exist.
        /// </summary>
        Task<bool> DeleteAsync(int id);
    }
}