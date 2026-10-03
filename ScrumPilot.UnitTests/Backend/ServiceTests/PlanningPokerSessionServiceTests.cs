using ScrumPilot.API.Services;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public class PlanningPokerSessionServiceTests
{
    private static readonly PlanningPokerSessionKey SessionKey = new(3, 7);

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

    [Fact]
    public void SessionsWithSameProjectIdInDifferentOrganizations_AreIsolated()
    {
        var service = new PlanningPokerSessionService();
        var otherOrganizationKey = new PlanningPokerSessionKey(4, SessionKey.ProjectId);
        service.AddParticipant("connection-1", "Alice", SessionKey);
        service.AddParticipant("connection-2", "Bob", otherOrganizationKey);

        service.SetCurrentPbi("connection-1", 42);
        service.SetVote("connection-1", 8);

        var first = service.GetStateForSession(SessionKey, includeVotes: true);
        var second = service.GetStateForSession(otherOrganizationKey, includeVotes: true);
        Assert.Equal(42, first.CurrentPbiId);
        Assert.Single(first.Participants);
        Assert.Null(second.CurrentPbiId);
        Assert.Single(second.Participants);
        Assert.Equal("Bob", second.Participants[0].DisplayName);
    }

    private static PlanningPokerSessionService CreateSession(int currentPbiId)
    {
        var service = new PlanningPokerSessionService();
        service.AddParticipant("connection-1", "Alice", SessionKey);
        service.SetCurrentPbi("connection-1", currentPbiId);
        service.SetVote("connection-1", points: 8);
        service.Reveal("connection-1");
        return service;
    }
}
