using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly ScrumPilotContext _ctx;

    public ProjectRepository(ScrumPilotContext ctx) => _ctx = ctx;

    public async Task<IEnumerable<Project>> GetAllProjectsAsync()
        => await _ctx.Projects.OrderBy(p => p.ProjectName).ToListAsync();

    public async Task<Project?> GetByIdAsync(int id)
        => await _ctx.Projects.FindAsync(id);

    public async Task<Project> AddAsync(Project project)
    {
        _ctx.Projects.Add(project);
        await _ctx.SaveChangesAsync();
        return project;
    }

    public async Task<Project> UpdateAsync(Project project)
    {
        _ctx.Projects.Update(project);
        await _ctx.SaveChangesAsync();
        return project;
    }

    public async Task DeleteAsync(int id)
    {
        var project = await _ctx.Projects.FindAsync(id);
        if (project != null)
        {
            _ctx.Projects.Remove(project);
            await _ctx.SaveChangesAsync();
        }
    }
}
