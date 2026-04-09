using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public interface IStoryRepository
    {
        Task<IEnumerable<ProductBacklogItem>> GetAllStoriesAsync();
        Task<ProductBacklogItem?> GetByIdAsync(int id);
        Task<ProductBacklogItem> AddAsync(ProductBacklogItem story);
        Task<ProductBacklogItem> UpdateAsync(ProductBacklogItem story);
        Task<bool> DeleteAsync(int id);
        Task<IEnumerable<ProductBacklogItem>> GetByStatusAsync(PbiStatus status);
        Task<IEnumerable<ProductBacklogItem>> GetDraftStoriesAsync();
        Task<IEnumerable<ProductBacklogItem>> GetNonDraftStoriesAsync();
    }
}