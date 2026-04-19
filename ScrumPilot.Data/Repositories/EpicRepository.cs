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
    }
}
