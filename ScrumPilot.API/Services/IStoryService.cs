using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IStoryService
    {
        Task<IEnumerable<Story>> GetAllStoriesAsync();
        Task<IEnumerable<Story>> GetDraftStoriesAsync();

        Task<List<Story>> GenerateAiStories(List<string> problemStatements);

        Task<Story> CreateStoryAsync(Story story);
        Task<Story> CommitStoryAsync(Story story);
        Task<Story> UpdateStoryAsync(Story story);
        Task<bool> DeleteStoryAsync(int id);
        Task<Story> CreateDraftStoryAsync(Story story);
        Task<IEnumerable<Story>> GetNonDraftStoriesAsync();
    }
}