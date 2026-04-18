using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

public interface IPbiHistoryRepository
{
    Task<IEnumerable<PbiStatusHistory>> GetHistoryForSprintAsync(int sprintId);
    Task<IEnumerable<PbiStatusHistory>> GetHistoryForPbisAsync(IEnumerable<int> pbiIds);
}
