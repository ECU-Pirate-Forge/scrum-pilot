using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public class EpicRepository : IEpicRepository
    {
        private readonly ScrumPilotContext _context;

        public EpicRepository(ScrumPilotContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId, CancellationToken cancellationToken = default)
        {
            return await _context.Epics
                .Where(e => e.ProjectId == projectId)
                .OrderBy(e => e.Name)
                .ToListAsync(cancellationToken);
        }

        public Task<Epic?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            _context.Epics.FirstOrDefaultAsync(e => e.EpicId == id, cancellationToken);

        public async Task<Epic> CreateAsync(Epic epic, CancellationToken cancellationToken = default)
        {
            _context.Epics.Add(epic);
            await _context.SaveChangesAsync(cancellationToken);
            return epic;
        }

        public async Task<Epic> UpdateAsync(Epic epic, CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return epic;
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await _context.Stories
                    .Where(p => p.EpicId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.EpicId, (int?)null), cancellationToken);

                var epic = await _context.Epics.FindAsync([id], cancellationToken);
                if (epic != null)
                {
                    _context.Epics.Remove(epic);
                    await _context.SaveChangesAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
