using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface ISprintService
    {
        Task<IEnumerable<Sprint>> GetAllSprintsAsync();
        Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId);
        Task<Sprint> CreateAsync(Sprint sprint);
        Task<Sprint> UpdateAsync(Sprint sprint);
        Task DeleteAsync(int id);
    }
}
