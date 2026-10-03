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
    public async Task JoinSession_AccessRevokedBeforeRegistration_LeavesNoStateGroupOrJoinMessages()
    {
        var setup = CreateHub();
        var groups = new TrackingGroupManager();
        setup.Hub.Groups = groups;
        var organizationLookupStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOrganizationLookup = new TaskCompletionSource<int?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var accessCall = 0;
        setup.Access.CanAccessProjectAsync("user-1", 7, setup.CancellationToken)
            .Returns(_ => Task.FromResult(Interlocked.Increment(ref accessCall) == 1));
        setup.Access.GetOrganizationIdForProjectAsync(7, setup.CancellationToken)
            .Returns(async _ =>
            {
                organizationLookupStarted.TrySetResult();
                return await releaseOrganizationLookup.Task;
            });
        var evictor = CreateEvictor(setup, groups);

        var join = setup.Hub.JoinSession("ignored", projectId: 7);
        await organizationLookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await evictor.EvictUserFromProjectAsync("user-1", 7);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        releaseOrganizationLookup.SetResult(3);

        var exception = await Assert.ThrowsAsync<HubException>(() => join);

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        Assert.False(groups.IsMember(ConnectionId, Group));
        await AssertNoJoinMessagesAsync(setup);
    }

    [Fact]
    public async Task JoinSession_AccessRevokedAfterRegistrationBeforeGroupAdd_LeavesNoStateGroupOrJoinMessages()
    {
        var setup = CreateHub();
        var groupAddStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGroupAdd = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var groups = new TrackingGroupManager
        {
            BeforeAddAsync = async (_, _, _) =>
            {
                groupAddStarted.TrySetResult();
                await releaseGroupAdd.Task;
            }
        };
        setup.Hub.Groups = groups;
        var accessCall = 0;
        setup.Access.CanAccessProjectAsync("user-1", 7, setup.CancellationToken)
            .Returns(_ => Task.FromResult(Interlocked.Increment(ref accessCall) == 1));
        var evictor = CreateEvictor(setup, groups);

        var join = setup.Hub.JoinSession("ignored", projectId: 7);
        await groupAddStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionKey, setup.Service.GetSessionKey(ConnectionId));
        await evictor.EvictUserFromProjectAsync("user-1", 7);
        releaseGroupAdd.SetResult();

        var exception = await Assert.ThrowsAsync<HubException>(() => join);

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        Assert.False(groups.IsMember(ConnectionId, Group));
        await AssertNoJoinMessagesAsync(setup);
    }

    [Fact]
    public async Task JoinSession_AccessRevokedAfterGroupAddBeforePostCheck_LeavesNoStateGroupOrJoinMessages()
    {
        var setup = CreateHub();
        var groups = new TrackingGroupManager();
        setup.Hub.Groups = groups;
        var postCheckStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePostCheck = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var accessCall = 0;
        setup.Access.CanAccessProjectAsync("user-1", 7, setup.CancellationToken)
            .Returns(_ => Interlocked.Increment(ref accessCall) == 1
                ? Task.FromResult(true)
                : CompletePostCheckAsync());
        var evictor = CreateEvictor(setup, groups);

        var join = setup.Hub.JoinSession("ignored", projectId: 7);
        await postCheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionKey, setup.Service.GetSessionKey(ConnectionId));
        Assert.True(groups.IsMember(ConnectionId, Group));
        await evictor.EvictUserFromProjectAsync("user-1", 7);
        releasePostCheck.SetResult(false);

        var exception = await Assert.ThrowsAsync<HubException>(() => join);

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        Assert.False(groups.IsMember(ConnectionId, Group));
        await AssertNoJoinMessagesAsync(setup);

        async Task<bool> CompletePostCheckAsync()
        {
            postCheckStarted.TrySetResult();
            return await releasePostCheck.Task;
        }
    }

    [Fact]
    public async Task JoinSession_UsesAuthenticatedNameAndOrganizationScopedGroup()
    {
        var setup = CreateHub();

        await setup.Hub.JoinSession("Untrusted", projectId: 7);

        await setup.Access.Received(2).CanAccessProjectAsync(
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
            ConnectionId, Group, CancellationToken.None);
        Assert.Empty(setup.Service.GetStateForSession(SessionKey).Participants);
        Assert.Equal(new PlanningPokerSessionKey(4, 9), setup.Service.GetSessionKey(ConnectionId));
    }

    [Fact]
    public async Task JoinSession_DeniedRejoin_EvictsAuthorizedPreviousSession()
    {
        var setup = CreateHub();
        await setup.Hub.JoinSession("ignored", projectId: 7);
        setup.Access.CanAccessProjectAsync("user-1", 9, setup.CancellationToken).Returns(false);

        var exception = await Assert.ThrowsAsync<HubException>(
            () => setup.Hub.JoinSession("ignored", projectId: 9));

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        await setup.Groups.Received(1).RemoveFromGroupAsync(
            ConnectionId, Group, CancellationToken.None);
        await setup.GroupClient.Received(1).SendCoreAsync(
            "UserLeft",
            Arg.Is<object?[]>(arguments =>
                arguments.Length == 1 && (string)arguments[0]! == ConnectionId),
            CancellationToken.None);
    }

    [Fact]
    public async Task JoinSession_FirstGroupAddFailure_DoesNotLeaveParticipant()
    {
        var setup = CreateHub();
        setup.Groups.AddToGroupAsync(ConnectionId, Group, setup.CancellationToken)
            .Returns(Task.FromException(new InvalidOperationException("group add failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => setup.Hub.JoinSession("ignored", projectId: 7));

        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        await setup.Groups.Received(1).RemoveFromGroupAsync(
            ConnectionId, Group, CancellationToken.None);
    }

    [Fact]
    public async Task JoinSession_RejoinGroupAddFailure_CleansBothSessionsAndNotifiesPeers()
    {
        var setup = CreateHub();
        await setup.Hub.JoinSession("ignored", projectId: 7);
        setup.Access.CanAccessProjectAsync("user-1", 9, setup.CancellationToken).Returns(true);
        setup.Access.GetOrganizationIdForProjectAsync(9, setup.CancellationToken).Returns(4);
        var newGroup = "planning-poker-4-9";
        setup.Groups.AddToGroupAsync(ConnectionId, newGroup, setup.CancellationToken)
            .Returns(Task.FromException(new InvalidOperationException("group add failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => setup.Hub.JoinSession("ignored", projectId: 9));

        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        await setup.Groups.Received(1).RemoveFromGroupAsync(
            ConnectionId, Group, CancellationToken.None);
        await setup.Groups.Received(1).RemoveFromGroupAsync(
            ConnectionId, newGroup, CancellationToken.None);
        await setup.GroupClient.Received(2).SendCoreAsync(
            "UserLeft",
            Arg.Is<object?[]>(arguments =>
                arguments.Length == 1 && (string)arguments[0]! == ConnectionId),
            CancellationToken.None);
    }

    [Fact]
    public async Task JoinSession_RejoinRemoveGroupFailure_AbortsWithoutJoiningNewSession()
    {
        var setup = CreateHub();
        await setup.Hub.JoinSession("ignored", projectId: 7);
        setup.Access.CanAccessProjectAsync("user-1", 9, setup.CancellationToken).Returns(true);
        setup.Access.GetOrganizationIdForProjectAsync(9, setup.CancellationToken).Returns(4);
        var newGroup = "planning-poker-4-9";
        setup.Groups.RemoveFromGroupAsync(ConnectionId, Group, CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("group remove failed")));

        var exception = await Assert.ThrowsAsync<HubException>(
            () => setup.Hub.JoinSession("ignored", projectId: 9));

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        setup.Hub.Context.Received(1).Abort();
        await setup.Groups.DidNotReceive().AddToGroupAsync(
            ConnectionId, newGroup, Arg.Any<CancellationToken>());
        await setup.GroupClient.Received(1).SendCoreAsync(
            "UserLeft",
            Arg.Is<object?[]>(arguments =>
                arguments.Length == 1 && (string)arguments[0]! == ConnectionId),
            CancellationToken.None);
    }

    [Fact]
    public async Task JoinSession_CanceledGroupAdd_DoesNotLeaveParticipant()
    {
        var setup = CreateHub();
        setup.Groups.AddToGroupAsync(ConnectionId, Group, setup.CancellationToken)
            .Returns(Task.FromException(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => setup.Hub.JoinSession("ignored", projectId: 7));

        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        await setup.Groups.Received(1).RemoveFromGroupAsync(
            ConnectionId, Group, CancellationToken.None);
    }

    [Theory]
    [InlineData("SelectCard")]
    [InlineData("RevealCards")]
    [InlineData("SelectPbi")]
    [InlineData("ClearPbiIfSelected")]
    [InlineData("ResetVoting")]
    public async Task SessionOperation_RevokedAccess_EvictsWithoutMutationOrOperationBroadcast(
        string operation)
    {
        var setup = CreateJoinedHub(currentPbiId: 42);
        setup.Service.SetVote(ConnectionId, 5);
        setup.Service.AddParticipant("peer-2", "user-2", "Bob", SessionKey);
        setup.Service.SetVote("peer-2", 3);
        var before = setup.Service.GetStateForSession(SessionKey, includeVotes: true);
        setup.Access.CanAccessProjectAsync("user-1", 7, setup.CancellationToken).Returns(false);

        var exception = await Assert.ThrowsAsync<HubException>(() => operation switch
        {
            "SelectCard" => setup.Hub.SelectCard(8),
            "RevealCards" => setup.Hub.RevealCards(),
            "SelectPbi" => setup.Hub.SelectPbi(99),
            "ClearPbiIfSelected" => setup.Hub.ClearPbiIfSelected(42),
            "ResetVoting" => setup.Hub.ResetVoting(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        var after = setup.Service.GetStateForSession(SessionKey, includeVotes: true);
        Assert.Equal(before.CurrentPbiId, after.CurrentPbiId);
        Assert.Equal(before.Revealed, after.Revealed);
        var peer = Assert.Single(after.Participants);
        Assert.Equal("peer-2", peer.ConnectionId);
        Assert.True(peer.HasVoted);
        Assert.Equal(3, peer.Points);
        await setup.GroupClient.Received(1).SendCoreAsync(
            "UserLeft",
            Arg.Is<object?[]>(arguments =>
                arguments.Length == 1 && (string)arguments[0]! == ConnectionId),
            CancellationToken.None);
        await setup.Access.DidNotReceiveWithAnyArgs()
            .PbiBelongsToProjectAsync(default, default, default);
    }

    [Fact]
    public async Task SessionOperation_RevokedAccessAndGroupRemovalFailure_AbortsWithoutMutation()
    {
        var setup = CreateJoinedHub(currentPbiId: 42);
        setup.Service.SetVote(ConnectionId, 5);
        setup.Access.CanAccessProjectAsync("user-1", 7, setup.CancellationToken).Returns(false);
        setup.Groups.RemoveFromGroupAsync(ConnectionId, Group, CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("group remove failed")));

        var exception = await Assert.ThrowsAsync<HubException>(
            () => setup.Hub.SelectCard(8));

        Assert.Equal("Project not found.", exception.Message);
        Assert.Null(setup.Service.GetSessionKey(ConnectionId));
        setup.Hub.Context.Received(1).Abort();
        var state = setup.Service.GetStateForSession(SessionKey, includeVotes: true);
        Assert.Empty(state.Participants);
        await setup.GroupClient.Received(1).SendCoreAsync(
            "UserLeft",
            Arg.Is<object?[]>(arguments =>
                arguments.Length == 1 && (string)arguments[0]! == ConnectionId),
            CancellationToken.None);
        await setup.GroupClient.DidNotReceive().SendCoreAsync(
            "CardSelected",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
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
            CancellationToken.None);
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
            CancellationToken.None);
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
        setup.Service.AddParticipant(ConnectionId, "user-1", "Alice", SessionKey);
        if (currentPbiId.HasValue)
            setup.Service.SetCurrentPbi(ConnectionId, currentPbiId);
        return setup;
    }

    private static PlanningPokerConnectionEvictor CreateEvictor(
        HubSetup setup,
        IGroupManager groups)
    {
        var hubContext = Substitute.For<IHubContext<PlanningPokerHub>>();
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(setup.GroupClient);
        hubContext.Clients.Returns(clients);
        hubContext.Groups.Returns(groups);
        return new PlanningPokerConnectionEvictor(setup.Service, hubContext);
    }

    private static async Task AssertNoJoinMessagesAsync(HubSetup setup)
    {
        await setup.CallerClient.DidNotReceive().SendCoreAsync(
            "ReceiveSessionState",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
        await setup.OthersClient.DidNotReceive().SendCoreAsync(
            "UserJoined",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
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
            hub,
            service,
            access,
            groups,
            groupClient,
            callerClient,
            othersClient,
            cancellationToken);
    }

    private sealed record HubSetup(
        PlanningPokerHub Hub,
        PlanningPokerSessionService Service,
        IOrganizationAccessService Access,
        IGroupManager Groups,
        IClientProxy GroupClient,
        ISingleClientProxy CallerClient,
        IClientProxy OthersClient,
        CancellationToken CancellationToken);

    private sealed class TrackingGroupManager : IGroupManager
    {
        private readonly HashSet<(string ConnectionId, string GroupName)> _memberships = [];
        private readonly object _lock = new();

        public Func<string, string, CancellationToken, Task>? BeforeAddAsync { get; init; }

        public async Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            if (BeforeAddAsync is not null)
                await BeforeAddAsync(connectionId, groupName, cancellationToken);

            lock (_lock)
                _memberships.Add((connectionId, groupName));
        }

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            lock (_lock)
                _memberships.Remove((connectionId, groupName));
            return Task.CompletedTask;
        }

        public bool IsMember(string connectionId, string groupName)
        {
            lock (_lock)
                return _memberships.Contains((connectionId, groupName));
        }
    }
}
