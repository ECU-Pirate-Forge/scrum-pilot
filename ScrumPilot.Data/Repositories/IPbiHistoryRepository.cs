using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories;

/// <summary>
/// Data-access contract for reading <see cref="PbiStatusHistory"/> records used by metrics calculations.
/// </summary>
public interface IPbiHistoryRepository
{
    /// <summary>Returns all status-transition records for every PBI in the given sprint.</summary>
    Task<IEnumerable<PbiStatusHistory>> GetHistoryForSprintAsync(int sprintId);

    /// <summary>Returns all status-transition records for the given set of PBI IDs.</summary>
    Task<IEnumerable<PbiStatusHistory>> GetHistoryForPbisAsync(IEnumerable<int> pbiIds);
}
