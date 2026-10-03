using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.API.Services;

public readonly record struct PlanningPokerSessionKey(int OrganizationId, int ProjectId);

public class PlanningPokerSessionService
{
    private sealed class ProjectSession
    {
        public Dictionary<string, (string DisplayName, int? Points, bool HasVoted)> Participants = [];
        public int? CurrentPbiId;
        public bool Revealed;
    }

    private readonly Dictionary<PlanningPokerSessionKey, ProjectSession> _sessions = [];
    private readonly Dictionary<string, PlanningPokerSessionKey> _connectionToSession = [];
    private readonly object _lock = new();

    private ProjectSession GetOrCreateSession(PlanningPokerSessionKey sessionKey)
    {
        if (!_sessions.TryGetValue(sessionKey, out var session))
        {
            session = new ProjectSession();
            _sessions[sessionKey] = session;
        }
        return session;
    }

    public void AddParticipant(
        string connectionId,
        string displayName,
        PlanningPokerSessionKey sessionKey)
    {
        lock (_lock)
        {
            _connectionToSession[connectionId] = sessionKey;
            GetOrCreateSession(sessionKey).Participants[connectionId] = (displayName, null, false);
        }
    }

    public PlanningPokerSessionKey? RemoveParticipant(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var sessionKey))
                return null;
            _connectionToSession.Remove(connectionId);
            if (_sessions.TryGetValue(sessionKey, out var session))
                session.Participants.Remove(connectionId);
            return sessionKey;
        }
    }

    public PlanningPokerSessionKey? GetSessionKey(string connectionId)
    {
        lock (_lock)
            return _connectionToSession.TryGetValue(connectionId, out var key) ? key : null;
    }

    public void SetVote(string connectionId, int? points)
    {
        lock (_lock)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var sessionKey)) return;
            var session = GetOrCreateSession(sessionKey);
            if (session.Participants.ContainsKey(connectionId))
            {
                session.Participants[connectionId] =
                    (session.Participants[connectionId].DisplayName, points, true);
            }
        }
    }

    public void SetCurrentPbi(string connectionId, int? pbiId)
    {
        lock (_lock)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var sessionKey)) return;
            var session = GetOrCreateSession(sessionKey);
            session.CurrentPbiId = pbiId;
            session.Revealed = false;
            foreach (var key in session.Participants.Keys.ToList())
                session.Participants[key] = (session.Participants[key].DisplayName, null, false);
        }
    }

    public PokerSessionState? ClearCurrentPbiIfSelected(string connectionId, int expectedPbiId)
    {
        lock (_lock)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var sessionKey)) return null;
            var session = GetOrCreateSession(sessionKey);
            if (session.CurrentPbiId != expectedPbiId) return null;

            session.CurrentPbiId = null;
            session.Revealed = false;
            foreach (var key in session.Participants.Keys.ToList())
                session.Participants[key] = (session.Participants[key].DisplayName, null, false);

            return CreateState(session, includeVotes: false);
        }
    }

    public void Reveal(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var sessionKey)) return;
            GetOrCreateSession(sessionKey).Revealed = true;
        }
    }

    public void Reset(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var sessionKey)) return;
            var session = GetOrCreateSession(sessionKey);
            session.Revealed = false;
            foreach (var key in session.Participants.Keys.ToList())
                session.Participants[key] = (session.Participants[key].DisplayName, null, false);
        }
    }

    public PokerSessionState? GetState(string connectionId, bool includeVotes = false)
    {
        lock (_lock)
        {
            if (!_connectionToSession.TryGetValue(connectionId, out var sessionKey)) return null;
            return CreateState(GetOrCreateSession(sessionKey), includeVotes);
        }
    }

    public PokerSessionState GetStateForSession(
        PlanningPokerSessionKey sessionKey,
        bool includeVotes = false)
    {
        lock (_lock)
            return CreateState(GetOrCreateSession(sessionKey), includeVotes);
    }

    private static PokerSessionState CreateState(ProjectSession session, bool includeVotes)
    {
        var showVotes = includeVotes || session.Revealed;
        return new PokerSessionState
        {
            CurrentPbiId = session.CurrentPbiId,
            Revealed = session.Revealed,
            Participants = session.Participants.Select(kvp => new ParticipantState
            {
                ConnectionId = kvp.Key,
                DisplayName = kvp.Value.DisplayName,
                HasVoted = kvp.Value.HasVoted,
                Points = showVotes ? kvp.Value.Points : null
            }).ToList()
        };
    }
}
