using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public interface IStoryRepository
    {
        Task<IEnumerable<Story>> GetAllStoriesAsync();
        Task<Story?> GetByIdAsync(Guid id);
        Task<Story> AddAsync(Story story);
        Task<Story> UpdateAsync(Story story);
        Task<bool> DeleteAsync(Guid id);
        Task<IEnumerable<Story>> GetByStatusAsync(StoryStatus status);
        Task<IEnumerable<Story>> GetDraftStoriesAsync();
    }
}