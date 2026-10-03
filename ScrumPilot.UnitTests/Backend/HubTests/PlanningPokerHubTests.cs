using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.UnitTests.Backend.HubTests;

public class PlanningPokerHubTests
{
    [Fact]
    public async Task ClearPbiIfSelected_BroadcastsClearedStateForMatchingSelection()
    {
        var (hub, client, service) = CreateHub(currentPbiId: 42);

        await hub.ClearPbiIfSelected(expectedPbiId: 42);

        await client.Received(1).SendCoreAsync(
            "PbiSelected",
            Arg.Is<object?[]>(arguments =>
                arguments.Length == 1 &&
                ((PokerSessionState)arguments[0]!).CurrentPbiId == null &&
                !((PokerSessionState)arguments[0]!).Revealed),
            Arg.Any<CancellationToken>());
        Assert.Null(service.GetState("connection-1")!.CurrentPbiId);
    }

    [Fact]
    public async Task ClearPbiIfSelected_DoesNotBroadcastOrClearNewerSelection()
    {
        var (hub, client, service) = CreateHub(currentPbiId: 43);

        await hub.ClearPbiIfSelected(expectedPbiId: 42);

        await client.DidNotReceiveWithAnyArgs()
            .SendCoreAsync(default!, default!, default);
        Assert.Equal(43, service.GetState("connection-1")!.CurrentPbiId);
    }

    private static (PlanningPokerHub Hub, IClientProxy Client, PlanningPokerSessionService Service)
        CreateHub(int currentPbiId)
    {
        var service = new PlanningPokerSessionService();
        service.AddParticipant("connection-1", "Alice", projectId: 7);
        service.SetCurrentPbi("connection-1", currentPbiId);

        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection-1");

        var client = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubCallerClients>();
        clients.Group("planning-poker-7").Returns(client);

        var hub = new PlanningPokerHub(service)
        {
            Context = context,
            Clients = clients
        };

        return (hub, client, service);
    }
}
