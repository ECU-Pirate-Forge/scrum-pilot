using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IStoryService
    {
        Task<IEnumerable<Story>> GetAllStoriesAsync();
        Task<IEnumerable<Story>> GetDraftStoriesAsync();
        Task<Story> GenerateAiStory(string problemStatement);
    }
}