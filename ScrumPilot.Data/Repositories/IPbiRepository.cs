using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public interface IPbiRepository
    {
        Task<ProductBacklogItem> AddAsync(ProductBacklogItem story);
        Task<bool> DeleteAsync(int id);
        Task<IEnumerable<ProductBacklogItem>> GetAllPbisAsync();
        Task<ProductBacklogItem?> GetByIdAsync(int id);
        Task<IEnumerable<ProductBacklogItem>> GetByStatusAsync(PbiStatus status);
        Task<IEnumerable<ProductBacklogItem>> GetDraftPbisAsync();
        Task<IEnumerable<ProductBacklogItem>> GetNonDraftPbisAsync();
        Task<IEnumerable<ProductBacklogItem>> GetFilteredPbisAsync(int? sprintId, int? epicId, int? projectId = null);
        Task<ProductBacklogItem> UpdateAsync(ProductBacklogItem story);
    }
}