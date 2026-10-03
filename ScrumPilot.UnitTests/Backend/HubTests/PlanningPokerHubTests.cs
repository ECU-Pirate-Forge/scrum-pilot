using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.UnitTests.Backend.HubTests;

public class PlanningPokerHubTests
{
    private const string ConnectionId = "connection-1";
    private const string Group = "planning-poker-3-7";
    private static readonly PlanningPokerSessionKey SessionKey = new(3, 7);

    [Fact]
    public async Task JoinSession_RejectsUnauthenticatedCallerWithoutChangingSessionOrGroup()
    {
        var setup = CreateHub(authenticated: false);

        var exception = await Assert.ThrowsAsync<HubException>(
            () => setup.Hub.JoinSession("Untrusted", projectId: 7));

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        await setup.Groups.DidNotReceiveWithAnyArgs()
            .AddToGroupAsync(default!, default!, default);
        await setup.Access.DidNotReceiveWithAnyArgs()
            .CanAccessProjectAsync(default!, default, default);
    }

    [Fact]
    public async Task JoinSession_RejectsCallerWithoutProjectAccess()
    {
        var setup = CreateHub(canAccess: false);

        var exception = await Assert.ThrowsAsync<HubException>(
            () => setup.Hub.JoinSession("Untrusted", projectId: 7));

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        await setup.Groups.DidNotReceiveWithAnyArgs()
            .AddToGroupAsync(default!, default!, default);
    }

    [Fact]
    public async Task JoinSession_UsesAuthenticatedNameAndOrganizationScopedGroup()
    {
        var setup = CreateHub();

        await setup.Hub.JoinSession("Untrusted", projectId: 7);

        await setup.Access.Received(1).CanAccessProjectAsync(
            "user-1", 7, setup.CancellationToken);
        await setup.Access.Received(1).GetOrganizationIdForProjectAsync(
            7, setup.CancellationToken);
        await setup.Groups.Received(1).AddToGroupAsync(
            ConnectionId, Group, setup.CancellationToken);
        Assert.Equal(SessionKey, setup.Service.GetSessionKey(ConnectionId));
        var participant = Assert.Single(setup.Service.GetState(ConnectionId)!.Participants);
        Assert.Equal("Alice", participant.DisplayName);
    }

    [Fact]
    public async Task JoinSession_Twice_RemovesParticipantAndGroupFromPreviousSession()
    {
        var setup = CreateHub();
        await setup.Hub.JoinSession("ignored", projectId: 7);
        setup.Access.CanAccessProjectAsync("user-1", 9, setup.CancellationToken).Returns(true);
        setup.Access.GetOrganizationIdForProjectAsync(9, setup.CancellationToken).Returns(4);

        await setup.Hub.JoinSession("ignored", projectId: 9);

        await setup.Groups.Received(1).RemoveFromGroupAsync(
            ConnectionId, Group, setup.CancellationToken);
        Assert.Empty(setup.Service.GetStateForSession(SessionKey).Participants);
        Assert.Equal(new PlanningPokerSessionKey(4, 9), setup.Service.GetSessionKey(ConnectionId));
    }

    [Fact]
    public async Task SelectPbi_RejectsPbiFromAnotherProjectWithoutMutationOrBroadcast()
    {
        var setup = CreateJoinedHub(currentPbiId: 42);
        setup.Access.PbiBelongsToProjectAsync(99, 7, setup.CancellationToken).Returns(false);

        var exception = await Assert.ThrowsAsync<HubException>(() => setup.Hub.SelectPbi(99));

        Assert.Equal("Product backlog item not found.", exception.Message);
        Assert.Equal(42, setup.Service.GetState(ConnectionId)!.CurrentPbiId);
        await setup.GroupClient.DidNotReceiveWithAnyArgs()
            .SendCoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task SelectPbi_BroadcastsWhenPbiBelongsToJoinedProject()
    {
        var setup = CreateJoinedHub();
        setup.Access.PbiBelongsToProjectAsync(99, 7, setup.CancellationToken).Returns(true);

        await setup.Hub.SelectPbi(99);

        Assert.Equal(99, setup.Service.GetState(ConnectionId)!.CurrentPbiId);
        await setup.GroupClient.Received(1).SendCoreAsync(
            "PbiSelected",
            Arg.Is<object?[]>(arguments =>
                arguments.Length == 1 &&
                ((PokerSessionState)arguments[0]!).CurrentPbiId == 99),
            setup.CancellationToken);
    }

    [Fact]
    public async Task ClearPbiIfSelected_BroadcastsClearedStateForMatchingSelection()
    {
        var setup = CreateJoinedHub(currentPbiId: 42);

        await setup.Hub.ClearPbiIfSelected(expectedPbiId: 42);

        await setup.GroupClient.Received(1).SendCoreAsync(
            "PbiSelected",
            Arg.Is<object?[]>(arguments =>
                arguments.Length == 1 &&
                ((PokerSessionState)arguments[0]!).CurrentPbiId == null &&
                !((PokerSessionState)arguments[0]!).Revealed),
            setup.CancellationToken);
        Assert.Null(setup.Service.GetState(ConnectionId)!.CurrentPbiId);
    }

    [Fact]
    public async Task ClearPbiIfSelected_DoesNotBroadcastOrClearNewerSelection()
    {
        var setup = CreateJoinedHub(currentPbiId: 43);

        await setup.Hub.ClearPbiIfSelected(expectedPbiId: 42);

        await setup.GroupClient.DidNotReceiveWithAnyArgs()
            .SendCoreAsync(default!, default!, default);
        Assert.Equal(43, setup.Service.GetState(ConnectionId)!.CurrentPbiId);
    }

    [Fact]
    public async Task OnDisconnected_RemovesParticipantAndBroadcastsToOrganizationGroup()
    {
        var setup = CreateJoinedHub();
        setup.Hub.Context.ConnectionAborted.Returns(new CancellationToken(canceled: true));

        await setup.Hub.OnDisconnectedAsync(null);

        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        await setup.GroupClient.Received(1).SendCoreAsync(
            "UserLeft",
            Arg.Is<object?[]>(arguments => arguments.Length == 1 && (string)arguments[0]! == ConnectionId),
            CancellationToken.None);
    }

    private static HubSetup CreateJoinedHub(int? currentPbiId = null)
    {
        var setup = CreateHub();
        setup.Service.AddParticipant(ConnectionId, "Alice", SessionKey);
        if (currentPbiId.HasValue)
            setup.Service.SetCurrentPbi(ConnectionId, currentPbiId);
        return setup;
    }

    private static HubSetup CreateHub(bool authenticated = true, bool canAccess = true)
    {
        var cancellationToken = new CancellationTokenSource().Token;
        var service = new PlanningPokerSessionService();
        var access = Substitute.For<IOrganizationAccessService>();
        access.CanAccessProjectAsync("user-1", 7, cancellationToken).Returns(canAccess);
        access.GetOrganizationIdForProjectAsync(7, cancellationToken).Returns(3);

        var identity = authenticated
            ? new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-1"),
                    new Claim(ClaimTypes.Name, "Alice")
                ],
                "test")
            : new ClaimsIdentity();
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(ConnectionId);
        context.ConnectionAborted.Returns(cancellationToken);
        context.User.Returns(new ClaimsPrincipal(identity));

        var groupClient = Substitute.For<IClientProxy>();
        var callerClient = Substitute.For<ISingleClientProxy>();
        var othersClient = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubCallerClients>();
        clients.Group(Arg.Any<string>()).Returns(groupClient);
        clients.Caller.Returns(callerClient);
        clients.OthersInGroup(Arg.Any<string>()).Returns(othersClient);
        var groups = Substitute.For<IGroupManager>();

        var hub = new PlanningPokerHub(service, access)
        {
            Context = context,
            Clients = clients,
            Groups = groups
        };

        return new HubSetup(
            hub, service, access, groups, groupClient, cancellationToken);
    }

    private sealed record HubSetup(
        PlanningPokerHub Hub,
        PlanningPokerSessionService Service,
        IOrganizationAccessService Access,
        IGroupManager Groups,
        IClientProxy GroupClient,
        CancellationToken CancellationToken);
}
