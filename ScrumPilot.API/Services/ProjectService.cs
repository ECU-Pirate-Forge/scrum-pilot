using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>
/// Implements <see cref="IProjectService"/> by delegating to <see cref="IProjectRepository"/>.
/// </summary>
public class ProjectService : IProjectService
{
    private readonly IProjectRepository _repo;

    public ProjectService(IProjectRepository repo) => _repo = repo;

    public Task<IEnumerable<Project>> GetAllProjectsAsync() => _repo.GetAllProjectsAsync();
    public Task<Project?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);
    public Task<Project> CreateAsync(Project project) => _repo.AddAsync(project);
    public Task<Project> UpdateAsync(Project project) => _repo.UpdateAsync(project);
    public Task DeleteAsync(int id) => _repo.DeleteAsync(id);
}
