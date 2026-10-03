using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.API.Hubs;

[Authorize]
public class PlanningPokerHub(
    PlanningPokerSessionService session,
    IOrganizationAccessService accessService) : Hub
{
    private const string ProjectNotFoundMessage = "Project not found.";
    private const string PbiNotFoundMessage = "Product backlog item not found.";

    private static string GroupName(PlanningPokerSessionKey key) =>
        $"planning-poker-{key.OrganizationId}-{key.ProjectId}";

    public async Task JoinSession(string displayName, int projectId)
    {
        var cancellationToken = Context.ConnectionAborted;
        var principal = Context.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
            throw new HubException(ProjectNotFoundMessage);

        if (!await accessService.CanAccessProjectAsync(userId, projectId, cancellationToken))
            throw new HubException(ProjectNotFoundMessage);

        var organizationId = await accessService.GetOrganizationIdForProjectAsync(
            projectId,
            cancellationToken);
        if (!organizationId.HasValue)
            throw new HubException(ProjectNotFoundMessage);

        var previousKey = session.RemoveParticipant(Context.ConnectionId);
        if (previousKey.HasValue)
        {
            var previousGroup = GroupName(previousKey.Value);
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                previousGroup,
                cancellationToken);
            await Clients.Group(previousGroup).SendAsync(
                "UserLeft",
                Context.ConnectionId,
                cancellationToken);
        }

        var sessionKey = new PlanningPokerSessionKey(organizationId.Value, projectId);
        var trustedDisplayName = principal.Identity.Name;
        if (string.IsNullOrWhiteSpace(trustedDisplayName))
            trustedDisplayName = userId;

        session.AddParticipant(Context.ConnectionId, trustedDisplayName, sessionKey);
        var group = GroupName(sessionKey);
        await Groups.AddToGroupAsync(Context.ConnectionId, group, cancellationToken);

        var state = session.GetStateForSession(sessionKey);
        await Clients.Caller.SendAsync("ReceiveSessionState", state, cancellationToken);

        var newParticipant = new ParticipantState
        {
            ConnectionId = Context.ConnectionId,
            DisplayName = trustedDisplayName,
            HasVoted = false
        };
        await Clients.OthersInGroup(group).SendAsync(
            "UserJoined",
            newParticipant,
            cancellationToken);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var sessionKey = session.RemoveParticipant(Context.ConnectionId);
        if (sessionKey.HasValue)
        {
            await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
                "UserLeft",
                Context.ConnectionId,
                CancellationToken.None);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SelectCard(int? points)
    {
        var sessionKey = session.GetSessionKey(Context.ConnectionId);
        if (!sessionKey.HasValue) return;
        session.SetVote(Context.ConnectionId, points);
        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "CardSelected",
            Context.ConnectionId,
            Context.ConnectionAborted);
    }

    public async Task RevealCards()
    {
        var sessionKey = session.GetSessionKey(Context.ConnectionId);
        if (!sessionKey.HasValue) return;
        session.Reveal(Context.ConnectionId);
        var state = session.GetStateForSession(sessionKey.Value, includeVotes: true);
        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "CardsRevealed",
            state,
            Context.ConnectionAborted);
    }

    public async Task SelectPbi(int? pbiId)
    {
        var sessionKey = session.GetSessionKey(Context.ConnectionId);
        if (!sessionKey.HasValue) return;
        if (pbiId.HasValue
            && !await accessService.PbiBelongsToProjectAsync(
                pbiId.Value,
                sessionKey.Value.ProjectId,
                Context.ConnectionAborted))
        {
            throw new HubException(PbiNotFoundMessage);
        }

        session.SetCurrentPbi(Context.ConnectionId, pbiId);
        var state = session.GetStateForSession(sessionKey.Value);
        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "PbiSelected",
            state,
            Context.ConnectionAborted);
    }

    public async Task ClearPbiIfSelected(int expectedPbiId)
    {
        var sessionKey = session.GetSessionKey(Context.ConnectionId);
        if (!sessionKey.HasValue) return;

        var state = session.ClearCurrentPbiIfSelected(Context.ConnectionId, expectedPbiId);
        if (state is null) return;

        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "PbiSelected",
            state,
            Context.ConnectionAborted);
    }

    public async Task ResetVoting()
    {
        var sessionKey = session.GetSessionKey(Context.ConnectionId);
        if (!sessionKey.HasValue) return;
        session.Reset(Context.ConnectionId);
        var state = session.GetStateForSession(sessionKey.Value);
        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "VotingReset",
            state,
            Context.ConnectionAborted);
    }
}
