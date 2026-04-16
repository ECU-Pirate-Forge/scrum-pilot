using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public class EpicService : IEpicService
    {
        private readonly IEpicRepository _epicRepository;

        public EpicService(IEpicRepository epicRepository)
        {
            _epicRepository = epicRepository;
        }

        public async Task<IEnumerable<Epic>> GetAllEpicsAsync()
        {
            return await _epicRepository.GetAllEpicsAsync();
        }
    }
}
