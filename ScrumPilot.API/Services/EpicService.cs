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

        public async Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId)
        {
            return await _epicRepository.GetEpicsByProjectAsync(projectId);
        }

        public Task<Epic> CreateAsync(Epic epic) => _epicRepository.CreateAsync(epic);
        public Task<Epic> UpdateAsync(Epic epic) => _epicRepository.UpdateAsync(epic);
        public Task DeleteAsync(int id) => _epicRepository.DeleteAsync(id);
    }
}
