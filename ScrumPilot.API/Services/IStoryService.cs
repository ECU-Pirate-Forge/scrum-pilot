using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IStoryService
    {
        Task<IEnumerable<ProductBacklogItem>> GetAllStoriesAsync();
        Task<IEnumerable<ProductBacklogItem>> GetDraftStoriesAsync();

        Task<List<ProductBacklogItem>> GenerateAiStories(List<string> problemStatements);

        Task<ProductBacklogItem> CreateStoryAsync(ProductBacklogItem story);
        Task<ProductBacklogItem> CommitStoryAsync(ProductBacklogItem story);
        Task<ProductBacklogItem> UpdateStoryAsync(ProductBacklogItem story);
        Task<bool> DeleteStoryAsync(int id);
        Task<ProductBacklogItem> CreateDraftStoryAsync(ProductBacklogItem story);
        Task<IEnumerable<ProductBacklogItem>> GetNonDraftStoriesAsync();
    }
}