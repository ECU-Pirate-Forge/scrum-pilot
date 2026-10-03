using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    /// <summary>
    /// Data-access contract for <see cref="ProductBacklogItem"/> persistence.
    /// </summary>
    public interface IPbiRepository
    {
        Task<IEnumerable<ProductBacklogItem>> GetByProjectAsync(
            int projectId,
            bool? isDraft = null,
            CancellationToken cancellationToken = default);

        /// <summary>Returns the PBI with the given <paramref name="id"/>, or <c>null</c> if not found.</summary>
        Task<ProductBacklogItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns non-draft PBIs matching any combination of sprint, epic, and project filters.
        /// A <paramref name="sprintId"/> of <c>-1</c> returns PBIs with no sprint assigned.
        /// </summary>
        Task<IEnumerable<ProductBacklogItem>> GetFilteredPbisAsync(int? sprintId, int? epicId, int? projectId = null, CancellationToken cancellationToken = default);

        /// <summary>Persists a new PBI and returns it with its database-assigned ID.</summary>
        Task<ProductBacklogItem> AddAsync(ProductBacklogItem story, CancellationToken cancellationToken = default);

        Task<List<ProductBacklogItem>> AddRangeAsync(IEnumerable<ProductBacklogItem> stories, CancellationToken cancellationToken = default);

        /// <summary>Saves all changes to an existing PBI and returns the updated entity.</summary>
        Task<ProductBacklogItem> UpdateAsync(ProductBacklogItem story, CancellationToken cancellationToken = default);

        /// <summary>
        /// Permanently deletes the PBI with the given <paramref name="id"/>.
        /// Returns <c>true</c> if the record was found and removed; <c>false</c> if it did not exist.
        /// </summary>
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}