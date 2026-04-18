using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public class PbiHistoryRepository : IPbiHistoryRepository
{
    private readonly ScrumPilotContext _context;

    public PbiHistoryRepository(ScrumPilotContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<PbiStatusHistory>> GetHistoryForSprintAsync(int sprintId)
    {
        var pbiIds = await _context.Stories
            .Where(s => s.SprintId == sprintId)
            .Select(s => s.PbiId)
            .ToListAsync();

        return await _context.PbiStatusHistories
            .Where(h => pbiIds.Contains(h.PbiId))
            .OrderBy(h => h.ChangedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<PbiStatusHistory>> GetHistoryForPbisAsync(IEnumerable<int> pbiIds)
    {
        var ids = pbiIds.ToList();
        return await _context.PbiStatusHistories
            .Where(h => ids.Contains(h.PbiId))
            .OrderBy(h => h.ChangedAt)
            .ToListAsync();
    }
}
