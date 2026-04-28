using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IEpicService
    {
        Task<IEnumerable<Epic>> GetAllEpicsAsync();
    }
}
