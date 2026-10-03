using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class PlanningPokerConnectionEvictorTests
{
    [Fact]
    public async Task EvictUserFromProjectAsync_PassivelyRemovesGroupsAndBroadcastsUserLeft()
    {
        var sessions = new PlanningPokerSessionService();
        var key = new PlanningPokerSessionKey(3, 7);
        sessions.AddParticipant("removed", "user-1", "Alice", key);
        sessions.AddParticipant("remaining", "user-2", "Bob", key);
        var hub = Substitute.For<IHubContext<PlanningPokerHub>>();
        var groups = Substitute.For<IGroupManager>();
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        hub.Groups.Returns(groups);
        hub.Clients.Returns(clients);
        clients.Group("planning-poker-3-7").Returns(proxy);
        var evictor = new PlanningPokerConnectionEvictor(sessions, hub);

        await evictor.EvictUserFromProjectAsync("user-1", 7);

        await groups.Received(1).RemoveFromGroupAsync(
            "removed",
            "planning-poker-3-7",
            Arg.Any<CancellationToken>());
        await proxy.Received(1).SendCoreAsync(
            "UserLeft",
            Arg.Is<object?[]>(args => (string)args[0]! == "removed"),
            Arg.Any<CancellationToken>());
        Assert.Null(sessions.GetSessionKey("removed"));
        Assert.Equal(key, sessions.GetSessionKey("remaining"));
    }
}
