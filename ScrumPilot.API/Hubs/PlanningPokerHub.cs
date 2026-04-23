using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.API.Hubs;

[Authorize]
public class PlanningPokerHub : Hub
{
    private readonly PlanningPokerSessionService _session;

    public PlanningPokerHub(PlanningPokerSessionService session)
    {
        _session = session;
    }

    private static string GroupName(int projectId) => $"planning-poker-{projectId}";

    public async Task JoinSession(string displayName, int projectId)
    {
        _session.AddParticipant(Context.ConnectionId, displayName, projectId);
        var group = GroupName(projectId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        var state = _session.GetStateForProject(projectId);
        await Clients.Caller.SendAsync("ReceiveSessionState", state);

        var newParticipant = new ParticipantState
        {
            ConnectionId = Context.ConnectionId,
            DisplayName = displayName,
            HasVoted = false
        };
        await Clients.OthersInGroup(group).SendAsync("UserJoined", newParticipant);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var projectId = _session.RemoveParticipant(Context.ConnectionId);
        if (projectId.HasValue)
            await Clients.Group(GroupName(projectId.Value)).SendAsync("UserLeft", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SelectCard(int? points)
    {
        var projectId = _session.GetProjectId(Context.ConnectionId);
        if (!projectId.HasValue) return;
        _session.SetVote(Context.ConnectionId, points);
        await Clients.Group(GroupName(projectId.Value)).SendAsync("CardSelected", Context.ConnectionId);
    }

    public async Task RevealCards()
    {
        var projectId = _session.GetProjectId(Context.ConnectionId);
        if (!projectId.HasValue) return;
        _session.Reveal(Context.ConnectionId);
        var state = _session.GetStateForProject(projectId.Value, includeVotes: true);
        await Clients.Group(GroupName(projectId.Value)).SendAsync("CardsRevealed", state);
    }

    public async Task SelectPbi(int? pbiId)
    {
        var projectId = _session.GetProjectId(Context.ConnectionId);
        if (!projectId.HasValue) return;
        _session.SetCurrentPbi(Context.ConnectionId, pbiId);
        var state = _session.GetStateForProject(projectId.Value);
        await Clients.Group(GroupName(projectId.Value)).SendAsync("PbiSelected", state);
    }

    public async Task ResetVoting()
    {
        var projectId = _session.GetProjectId(Context.ConnectionId);
        if (!projectId.HasValue) return;
        _session.Reset(Context.ConnectionId);
        var state = _session.GetStateForProject(projectId.Value);
        await Clients.Group(GroupName(projectId.Value)).SendAsync("VotingReset", state);
    }
}
