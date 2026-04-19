using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public class SprintService : ISprintService
    {
        private readonly ISprintRepository _sprintRepository;

        public SprintService(ISprintRepository sprintRepository)
        {
            _sprintRepository = sprintRepository;
        }

        public async Task<IEnumerable<Sprint>> GetAllSprintsAsync()
        {
            return await _sprintRepository.GetAllSprintsAsync();
        }

        public async Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId)
        {
            return await _sprintRepository.GetSprintsByProjectAsync(projectId);
        }

        public Task<Sprint> CreateAsync(Sprint sprint) => _sprintRepository.CreateAsync(sprint);
        public Task<Sprint> UpdateAsync(Sprint sprint) => _sprintRepository.UpdateAsync(sprint);
        public Task DeleteAsync(int id) => _sprintRepository.DeleteAsync(id);
    }
}
