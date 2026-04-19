using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public interface IProjectRepository
{
    Task<IEnumerable<Project>> GetAllProjectsAsync();
    Task<Project?> GetByIdAsync(int id);
    Task<Project> AddAsync(Project project);
}
