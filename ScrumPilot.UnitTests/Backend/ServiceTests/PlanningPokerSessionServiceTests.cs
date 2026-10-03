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
        service.AddParticipant("connection-1", "user-1", "Alice", SessionKey);
        service.AddParticipant("connection-2", "user-2", "Bob", otherOrganizationKey);

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

    [Fact]
    public void RemoveParticipant_WithStaleExpectedSession_DoesNotRemoveCurrentMapping()
    {
        var service = new PlanningPokerSessionService();
        var currentKey = new PlanningPokerSessionKey(4, 9);
        service.AddParticipant("connection-1", "user-1", "Alice", currentKey);

        var removed = service.RemoveParticipant("connection-1", SessionKey);

        Assert.Null(removed);
        Assert.Equal(currentKey, service.GetSessionKey("connection-1"));
        Assert.Single(service.GetStateForSession(currentKey).Participants);
    }

    [Fact]
    public void RemoveUserFromProject_RemovesAllMatchingConnectionsAndLeavesOthers()
    {
        var service = new PlanningPokerSessionService();
        service.AddParticipant("connection-1", "revoked", "Alice", SessionKey);
        service.AddParticipant("connection-2", "revoked", "Alice mobile", SessionKey);
        service.AddParticipant("connection-3", "other", "Bob", SessionKey);

        var removed = service.RemoveUserFromProject("revoked", SessionKey.ProjectId);

        Assert.Equal(["connection-1", "connection-2"], removed.Select(x => x.ConnectionId).Order());
        Assert.All(removed, x => Assert.Equal(SessionKey, x.SessionKey));
        var remaining = service.GetStateForSession(SessionKey).Participants;
        Assert.Single(remaining);
        Assert.Equal("connection-3", remaining[0].ConnectionId);
        Assert.Null(service.GetSessionKey("connection-1"));
        Assert.Null(service.GetSessionKey("connection-2"));
    }

    private static PlanningPokerSessionService CreateSession(int currentPbiId)
    {
        var service = new PlanningPokerSessionService();
        service.AddParticipant("connection-1", "user-1", "Alice", SessionKey);
        service.SetCurrentPbi("connection-1", currentPbiId);
        service.SetVote("connection-1", points: 8);
        service.Reveal("connection-1");
        return service;
    }
}
