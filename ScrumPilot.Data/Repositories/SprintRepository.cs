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
    }
}
