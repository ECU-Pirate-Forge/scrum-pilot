using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public class SprintRepository : ISprintRepository
    {
        private readonly ScrumPilotContext _context;

        public SprintRepository(ScrumPilotContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Sprint>> GetAllSprintsAsync()
        {
            return await _context.Sprints
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId)
        {
            return await _context.Sprints
                .Where(s => s.ProjectId == projectId)
                .OrderByDescending(s => s.StartDate)
                .ToListAsync();
        }

        public async Task<Sprint> CreateAsync(Sprint sprint)
        {
            _context.Sprints.Add(sprint);
            await _context.SaveChangesAsync();
            return sprint;
        }

        public async Task<Sprint> UpdateAsync(Sprint sprint)
        {
            _context.Sprints.Update(sprint);
            await _context.SaveChangesAsync();
            return sprint;
        }

        public async Task DeleteAsync(int id)
        {
            await _context.Stories
                .Where(p => p.SprintId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.SprintId, (int?)null));

            var sprint = await _context.Sprints.FindAsync(id);
            if (sprint != null)
            {
                _context.Sprints.Remove(sprint);
                await _context.SaveChangesAsync();
            }
        }
    }
}
