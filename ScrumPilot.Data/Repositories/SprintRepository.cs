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

        public async Task<IEnumerable<Sprint>> GetSprintsByProjectAsync(int projectId, CancellationToken cancellationToken = default)
        {
            return await _context.Sprints
                .Where(s => s.ProjectId == projectId)
                .OrderByDescending(s => s.StartDate)
                .ToListAsync(cancellationToken);
        }

        public Task<Sprint?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            _context.Sprints.FirstOrDefaultAsync(s => s.SprintId == id, cancellationToken);

        public async Task<Sprint> CreateAsync(Sprint sprint, CancellationToken cancellationToken = default)
        {
            _context.Sprints.Add(sprint);
            await _context.SaveChangesAsync(cancellationToken);
            return sprint;
        }

        public async Task<Sprint> UpdateAsync(Sprint sprint, CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return sprint;
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await _context.Stories
                .Where(p => p.SprintId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.SprintId, (int?)null), cancellationToken);

            var sprint = await _context.Sprints.FindAsync([id], cancellationToken);
            if (sprint != null)
            {
                _context.Sprints.Remove(sprint);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
