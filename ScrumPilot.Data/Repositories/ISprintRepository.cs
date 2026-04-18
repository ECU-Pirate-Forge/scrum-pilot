using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public interface ISprintRepository
    {
        Task<IEnumerable<Sprint>> GetAllSprintsAsync();
    }
}
