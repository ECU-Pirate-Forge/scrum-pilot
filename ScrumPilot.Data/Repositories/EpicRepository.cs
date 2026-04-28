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

        public async Task<IEnumerable<Epic>> GetAllEpicsAsync()
        {
            return await _context.Epics
                .OrderBy(e => e.Name)
                .ToListAsync();
        }

        public async Task<IEnumerable<Epic>> GetEpicsByProjectAsync(int projectId)
        {
            return await _context.Epics
                .Where(e => e.ProjectId == projectId)
                .OrderBy(e => e.Name)
                .ToListAsync();
        }

        public async Task<Epic> CreateAsync(Epic epic)
        {
            _context.Epics.Add(epic);
            await _context.SaveChangesAsync();
            return epic;
        }

        public async Task<Epic> UpdateAsync(Epic epic)
        {
            _context.Epics.Update(epic);
            await _context.SaveChangesAsync();
            return epic;
        }

        public async Task DeleteAsync(int id)
        {
            await _context.Stories
                .Where(p => p.EpicId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EpicId, (int?)null));

            var epic = await _context.Epics.FindAsync(id);
            if (epic != null)
            {
                _context.Epics.Remove(epic);
                await _context.SaveChangesAsync();
            }
        }
    }
}
