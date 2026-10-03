using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests;

public class PlanningPokerSaveGuardTests
{
    private readonly object _capturedHub = new();

    [Fact]
    public void ShouldClearSelection_WhenCapturedSelectionWasSavedAndIsNowAbsent()
    {
        var result = PlanningPokerSaveGuard.ShouldClearSelection(
            savedPbiId: 42,
            capturedPbiId: 42,
            capturedProjectId: 7,
            capturedHub: _capturedHub,
            currentPbiId: 42,
            currentProjectId: 7,
            currentHub: _capturedHub,
            refreshedPbiIds: [41, 43]);

        Assert.True(result);
    }

    [Theory]
    [InlineData(43, 7, false, false)]
    [InlineData(42, 8, false, false)]
    [InlineData(42, 7, true, false)]
    [InlineData(42, 7, false, true)]
    public void ShouldNotClearSelection_WhenSaveContextChangedOrSavedPbiRemains(
        int currentPbiId,
        int currentProjectId,
        bool replaceHub,
        bool savedPbiRemains)
    {
        var result = PlanningPokerSaveGuard.ShouldClearSelection(
            savedPbiId: 42,
            capturedPbiId: 42,
            capturedProjectId: 7,
            capturedHub: _capturedHub,
            currentPbiId,
            currentProjectId,
            currentHub: replaceHub ? new object() : _capturedHub,
            refreshedPbiIds: savedPbiRemains ? [42, 43] : [43]);

        Assert.False(result);
    }

    [Fact]
    public void ShouldNotClearSelection_WhenSavedPbiWasNotCapturedSelection()
    {
        var result = PlanningPokerSaveGuard.ShouldClearSelection(
            savedPbiId: 43,
            capturedPbiId: 42,
            capturedProjectId: 7,
            capturedHub: _capturedHub,
            currentPbiId: 42,
            currentProjectId: 7,
            currentHub: _capturedHub,
            refreshedPbiIds: [41]);

        Assert.False(result);
    }
}
