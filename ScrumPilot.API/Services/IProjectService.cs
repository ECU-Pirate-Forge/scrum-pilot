using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public interface IProjectService
{
    Task<IEnumerable<Project>> GetAllProjectsAsync();
    Task<Project?> GetByIdAsync(int id);
    Task<Project> CreateAsync(Project project);
}
