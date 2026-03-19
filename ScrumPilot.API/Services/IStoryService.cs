using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IStoryService
    {
        List<Story> GetStories();

        List<Story> GetDraftStories();

        Task<List<Story>> GenerateAiStory(List<string> problemStatements);
    }
}