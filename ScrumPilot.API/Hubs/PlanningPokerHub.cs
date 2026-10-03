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

    private string? GetAuthenticatedUserId()
    {
        var principal = Context.User;
        return principal?.Identity?.IsAuthenticated == true
            ? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
    }

    private async Task RemoveFromSessionAsync(PlanningPokerSessionKey? expectedKey = null)
    {
        var removedKey = session.RemoveParticipant(Context.ConnectionId, expectedKey);
        if (!removedKey.HasValue)
            return;

        var group = GroupName(removedKey.Value);
        try
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                group,
                CancellationToken.None);
        }
        catch (Exception)
        {
            // Continue so peers still receive the deterministic departure notification.
        }

        try
        {
            await Clients.Group(group).SendAsync(
                "UserLeft",
                Context.ConnectionId,
                CancellationToken.None);
        }
        catch (Exception)
        {
            // Session state is already clean; fanout is best effort.
        }
    }

    private async Task<PlanningPokerSessionKey?> GetAuthorizedSessionKeyAsync()
    {
        var sessionKey = session.GetSessionKey(Context.ConnectionId);
        if (!sessionKey.HasValue)
            return null;

        var userId = GetAuthenticatedUserId();
        var canAccess = false;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            try
            {
                canAccess = await accessService.CanAccessProjectAsync(
                    userId,
                    sessionKey.Value.ProjectId,
                    Context.ConnectionAborted);
            }
            catch (OperationCanceledException)
            {
                // Treat an interrupted authorization check as denied.
            }
        }

        if (canAccess)
            return sessionKey;

        await RemoveFromSessionAsync(sessionKey);
        throw new HubException(ProjectNotFoundMessage);
    }

    public async Task JoinSession(string displayName, int projectId)
    {
        var cancellationToken = Context.ConnectionAborted;
        var principal = Context.User;
        var userId = GetAuthenticatedUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            await RemoveFromSessionAsync();
            throw new HubException(ProjectNotFoundMessage);
        }

        bool canAccess;
        try
        {
            canAccess = await accessService.CanAccessProjectAsync(
                userId,
                projectId,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await RemoveFromSessionAsync();
            throw new HubException(ProjectNotFoundMessage);
        }

        if (!canAccess)
        {
            await RemoveFromSessionAsync();
            throw new HubException(ProjectNotFoundMessage);
        }

        var organizationId = await accessService.GetOrganizationIdForProjectAsync(
            projectId,
            cancellationToken);
        if (!organizationId.HasValue)
        {
            await RemoveFromSessionAsync();
            throw new HubException(ProjectNotFoundMessage);
        }

        await RemoveFromSessionAsync();

        var sessionKey = new PlanningPokerSessionKey(organizationId.Value, projectId);
        var trustedDisplayName = principal.Identity.Name;
        if (string.IsNullOrWhiteSpace(trustedDisplayName))
            trustedDisplayName = userId;

        session.AddParticipant(Context.ConnectionId, trustedDisplayName, sessionKey);
        var group = GroupName(sessionKey);
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group, cancellationToken);
        }
        catch
        {
            await RemoveFromSessionAsync(sessionKey);
            throw;
        }

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
            CancellationToken.None);
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
        var sessionKey = await GetAuthorizedSessionKeyAsync();
        if (!sessionKey.HasValue) return;
        session.SetVote(Context.ConnectionId, points);
        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "CardSelected",
            Context.ConnectionId,
            CancellationToken.None);
    }

    public async Task RevealCards()
    {
        var sessionKey = await GetAuthorizedSessionKeyAsync();
        if (!sessionKey.HasValue) return;
        session.Reveal(Context.ConnectionId);
        var state = session.GetStateForSession(sessionKey.Value, includeVotes: true);
        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "CardsRevealed",
            state,
            CancellationToken.None);
    }

    public async Task SelectPbi(int? pbiId)
    {
        var sessionKey = await GetAuthorizedSessionKeyAsync();
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
            CancellationToken.None);
    }

    public async Task ClearPbiIfSelected(int expectedPbiId)
    {
        var sessionKey = await GetAuthorizedSessionKeyAsync();
        if (!sessionKey.HasValue) return;

        var state = session.ClearCurrentPbiIfSelected(Context.ConnectionId, expectedPbiId);
        if (state is null) return;

        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "PbiSelected",
            state,
            CancellationToken.None);
    }

    public async Task ResetVoting()
    {
        var sessionKey = await GetAuthorizedSessionKeyAsync();
        if (!sessionKey.HasValue) return;
        session.Reset(Context.ConnectionId);
        var state = session.GetStateForSession(sessionKey.Value);
        await Clients.Group(GroupName(sessionKey.Value)).SendAsync(
            "VotingReset",
            state,
            CancellationToken.None);
    }
}
