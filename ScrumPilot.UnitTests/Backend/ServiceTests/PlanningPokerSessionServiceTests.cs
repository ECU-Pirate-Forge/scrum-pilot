using ScrumPilot.API.Services;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public class PlanningPokerSessionServiceTests
{
    [Fact]
    public void ClearCurrentPbiIfSelected_ClearsMatchingSelectionAndResetsVoting()
    {
        var service = CreateSession(currentPbiId: 42);

        var state = service.ClearCurrentPbiIfSelected("connection-1", expectedPbiId: 42);

        Assert.NotNull(state);
        Assert.Null(state.CurrentPbiId);
        Assert.False(state.Revealed);
        var participant = Assert.Single(state.Participants);
        Assert.False(participant.HasVoted);
        Assert.Null(participant.Points);
    }

    [Fact]
    public void ClearCurrentPbiIfSelected_DoesNotClearNewerSelection()
    {
        var service = CreateSession(currentPbiId: 43);

        var result = service.ClearCurrentPbiIfSelected("connection-1", expectedPbiId: 42);

        Assert.Null(result);
        var state = service.GetState("connection-1", includeVotes: true);
        Assert.NotNull(state);
        Assert.Equal(43, state.CurrentPbiId);
        Assert.True(state.Revealed);
        var participant = Assert.Single(state.Participants);
        Assert.True(participant.HasVoted);
        Assert.Equal(8, participant.Points);
    }

    private static PlanningPokerSessionService CreateSession(int currentPbiId)
    {
        var service = new PlanningPokerSessionService();
        service.AddParticipant("connection-1", "Alice", projectId: 7);
        service.SetCurrentPbi("connection-1", currentPbiId);
        service.SetVote("connection-1", points: 8);
        service.Reveal("connection-1");
        return service;
    }
}
