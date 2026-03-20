using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IStoryService
    {
        Task<IEnumerable<Story>> GetAllStoriesAsync();
        Task<IEnumerable<Story>> GetDraftStoriesAsync();
        Task<Story> GenerateAiStory(string problemStatement);

        Task<Story> CreateStoryAsync(Story story);
        Task<Story> UpdateStoryAsync(Story story);
        Task<bool> DeleteStoryAsync(Guid id);

    }
}