using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IEpicService
    {
        Task<IEnumerable<Epic>> GetAllEpicsAsync();
        Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId);
    }
}
