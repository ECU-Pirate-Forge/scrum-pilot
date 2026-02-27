using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IStoryService
    {
        List<Story> GetStories();
        Task<Story> GenerateAiStory(string problemStatement);
    }
}