namespace ScrumPilot.Web.Pages;

public static class PlanningPokerSaveGuard
{
    public static bool ShouldClearSelection(
        int savedPbiId,
        int? capturedPbiId,
        int? capturedProjectId,
        object? capturedHub,
        int? currentPbiId,
        int? currentProjectId,
        object? currentHub,
        IReadOnlyCollection<int> refreshedPbiIds)
    {
        return savedPbiId == capturedPbiId
            && currentPbiId == capturedPbiId
            && currentProjectId == capturedProjectId
            && ReferenceEquals(currentHub, capturedHub)
            && !refreshedPbiIds.Contains(savedPbiId);
    }
}
