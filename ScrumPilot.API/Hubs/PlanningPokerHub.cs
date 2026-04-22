using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.API.Hubs;

[Authorize]
public class PlanningPokerHub : Hub
{
    private const string SessionGroup = "planning-poker";
    private readonly PlanningPokerSessionService _session;

    public PlanningPokerHub(PlanningPokerSessionService session)
    {
        _session = session;
    }

    public async Task JoinSession(string displayName)
    {
        _session.AddParticipant(Context.ConnectionId, displayName);
        await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup);

        var state = _session.GetState();
        await Clients.Caller.SendAsync("ReceiveSessionState", state);

        var newParticipant = new ParticipantState
        {
            ConnectionId = Context.ConnectionId,
            DisplayName = displayName,
            HasVoted = false
        };
        await Clients.OthersInGroup(SessionGroup).SendAsync("UserJoined", newParticipant);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _session.RemoveParticipant(Context.ConnectionId);
        await Clients.Group(SessionGroup).SendAsync("UserLeft", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SelectCard(int? points)
    {
        _session.SetVote(Context.ConnectionId, points);
        await Clients.Group(SessionGroup).SendAsync("CardSelected", Context.ConnectionId);
    }

    public async Task RevealCards()
    {
        _session.Reveal();
        var state = _session.GetState(includeVotes: true);
        await Clients.Group(SessionGroup).SendAsync("CardsRevealed", state);
    }

    public async Task SelectPbi(int? pbiId)
    {
        _session.SetCurrentPbi(pbiId);
        var state = _session.GetState();
        await Clients.Group(SessionGroup).SendAsync("PbiSelected", state);
    }

    public async Task ResetVoting()
    {
        _session.Reset();
        var state = _session.GetState();
        await Clients.Group(SessionGroup).SendAsync("VotingReset", state);
    }
}
