using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public interface IEpicRepository
    {
        Task<IEnumerable<Epic>> GetAllEpicsAsync();
        Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId);
        Task<Epic> CreateAsync(Epic epic);
        Task<Epic> UpdateAsync(Epic epic);
        Task DeleteAsync(int id);
    }
}
