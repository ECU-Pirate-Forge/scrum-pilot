using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public interface ISprintRepository
    {
        Task<IEnumerable<Sprint>> GetAllSprintsAsync();
        Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId);
        Task<Sprint> CreateAsync(Sprint sprint);
        Task<Sprint> UpdateAsync(Sprint sprint);
        Task DeleteAsync(int id);
    }
}
