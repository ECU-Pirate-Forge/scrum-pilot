namespace ScrumPilot.FluentWeb.Services.PlanningPoker;

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
        IReadOnlyCollection<int> refreshedPbiIds,
        int? capturedOrganizationId = null,
        int? currentOrganizationId = null)
    {
        return savedPbiId == capturedPbiId
            && capturedOrganizationId == currentOrganizationId
            && currentPbiId == capturedPbiId
            && currentProjectId == capturedProjectId
            && ReferenceEquals(currentHub, capturedHub)
            && !refreshedPbiIds.Contains(savedPbiId);
    }
}
