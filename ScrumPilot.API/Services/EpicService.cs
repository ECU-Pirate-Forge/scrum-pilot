using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    /// <summary>
    /// Implements <see cref="IEpicService"/> by delegating to <see cref="IEpicRepository"/>.
    /// </summary>
    public class EpicService : IEpicService
    {
        private readonly IEpicRepository _epicRepository;

        public EpicService(IEpicRepository epicRepository)
        {
            _epicRepository = epicRepository;
        }

        public async Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId, CancellationToken cancellationToken = default)
        {
            return await _epicRepository.GetEpicsByProjectAsync(projectId, cancellationToken);
        }

        public Task<Epic> CreateAsync(Epic epic, CancellationToken cancellationToken = default) => _epicRepository.CreateAsync(epic, cancellationToken);
        public Task<Epic> UpdateAsync(Epic epic, CancellationToken cancellationToken = default) => _epicRepository.UpdateAsync(epic, cancellationToken);
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => _epicRepository.DeleteAsync(id, cancellationToken);
    }
}
