using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IStoryService
    {
        List<Story> GetStories();

        List<Story> GetDraftStories();

        Task<Story> GenerateAiStory(string problemStatement);
    }
}