using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public class ProjectService : IProjectService
{
    private readonly IProjectRepository _repo;

    public ProjectService(IProjectRepository repo) => _repo = repo;

    public Task<IEnumerable<Project>> GetAllProjectsAsync() => _repo.GetAllProjectsAsync();
    public Task<Project?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);
    public Task<Project> CreateAsync(Project project) => _repo.AddAsync(project);
}
