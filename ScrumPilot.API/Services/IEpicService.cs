using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IEpicService
    {
        Task<IEnumerable<Epic>> GetAllEpicsAsync();
        Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId);
        Task<Epic> CreateAsync(Epic epic);
        Task<Epic> UpdateAsync(Epic epic);
        Task DeleteAsync(int id);
    }
}
