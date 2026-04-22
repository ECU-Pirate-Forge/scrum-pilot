using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.API.Services;

public class PlanningPokerSessionService
{
    private readonly Dictionary<string, (string DisplayName, int? Points, bool HasVoted)> _participants = [];
    private int? _currentPbiId;
    private bool _revealed;
    private readonly object _lock = new();

    public void AddParticipant(string connectionId, string displayName)
    {
        lock (_lock) _participants[connectionId] = (displayName, null, false);
    }

    public void RemoveParticipant(string connectionId)
    {
        lock (_lock) _participants.Remove(connectionId);
    }

    public void SetVote(string connectionId, int? points)
    {
        lock (_lock)
        {
            if (_participants.ContainsKey(connectionId))
                _participants[connectionId] = (_participants[connectionId].DisplayName, points, true);
        }
    }

    public void SetCurrentPbi(int? pbiId)
    {
        lock (_lock)
        {
            _currentPbiId = pbiId;
            _revealed = false;
            foreach (var key in _participants.Keys.ToList())
                _participants[key] = (_participants[key].DisplayName, null, false);
        }
    }

    public void Reveal()
    {
        lock (_lock) _revealed = true;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _revealed = false;
            foreach (var key in _participants.Keys.ToList())
                _participants[key] = (_participants[key].DisplayName, null, false);
        }
    }

    public PokerSessionState GetState(bool includeVotes = false)
    {
        lock (_lock)
        {
            var showVotes = includeVotes || _revealed;
            return new PokerSessionState
            {
                CurrentPbiId = _currentPbiId,
                Revealed = _revealed,
                Participants = _participants.Select(kvp => new ParticipantState
                {
                    ConnectionId = kvp.Key,
                    DisplayName = kvp.Value.DisplayName,
                    HasVoted = kvp.Value.HasVoted,
                    Points = showVotes ? kvp.Value.Points : null
                }).ToList()
            };
        }
    }
}
