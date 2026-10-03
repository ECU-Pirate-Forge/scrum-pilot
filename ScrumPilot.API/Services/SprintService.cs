using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    /// <summary>
    /// Implements <see cref="ISprintService"/> by delegating to <see cref="ISprintRepository"/>.
    /// </summary>
    public class SprintService : ISprintService
    {
        private readonly ISprintRepository _sprintRepository;

        public SprintService(ISprintRepository sprintRepository)
        {
            _sprintRepository = sprintRepository;
        }

        public async Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId, CancellationToken cancellationToken = default)
        {
            return await _sprintRepository.GetSprintsByProjectAsync(projectId, cancellationToken);
        }

        public Task<Sprint> CreateAsync(Sprint sprint, CancellationToken cancellationToken = default) => _sprintRepository.CreateAsync(sprint, cancellationToken);
        public Task<Sprint> UpdateAsync(Sprint sprint, CancellationToken cancellationToken = default) => _sprintRepository.UpdateAsync(sprint, cancellationToken);
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => _sprintRepository.DeleteAsync(id, cancellationToken);
    }
}
