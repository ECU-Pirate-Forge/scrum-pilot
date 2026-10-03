using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.API.Services;

public readonly record struct PlanningPokerSessionKey(int OrganizationId, int ProjectId);
public readonly record struct PlanningPokerConnection(
    string ConnectionId,
    string UserId,
    PlanningPokerSessionKey SessionKey);

public class PlanningPokerSessionService
{
    private sealed class ProjectSession
    {
        public Dictionary<string, (string DisplayName, int? Points, bool HasVoted)> Participants = [];
        public int? CurrentPbiId;
        public bool Revealed;
    }

    private readonly Dictionary<PlanningPokerSessionKey, ProjectSession> _sessions = [];
    private readonly Dictionary<string, PlanningPokerConnection> _connections = [];
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
        string userId,
        string displayName,
        PlanningPokerSessionKey sessionKey)
    {
        lock (_lock)
        {
            _connections[connectionId] = new(connectionId, userId, sessionKey);
            GetOrCreateSession(sessionKey).Participants[connectionId] = (displayName, null, false);
        }
    }

    public PlanningPokerSessionKey? RemoveParticipant(
        string connectionId,
        PlanningPokerSessionKey? expectedSessionKey = null)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(connectionId, out var connection))
                return null;
            var sessionKey = connection.SessionKey;
            if (expectedSessionKey.HasValue && sessionKey != expectedSessionKey.Value)
                return null;
            _connections.Remove(connectionId);
            if (_sessions.TryGetValue(sessionKey, out var session))
                session.Participants.Remove(connectionId);
            return sessionKey;
        }
    }

    public IReadOnlyList<PlanningPokerConnection> RemoveUserFromProject(
        string userId,
        int projectId) =>
        RemoveConnections(x => x.UserId == userId && x.SessionKey.ProjectId == projectId);

    public IReadOnlyList<PlanningPokerConnection> RemoveUserFromOrganization(
        string userId,
        int organizationId) =>
        RemoveConnections(x =>
            x.UserId == userId && x.SessionKey.OrganizationId == organizationId);

    public IReadOnlyList<PlanningPokerConnection> RemoveProject(int projectId) =>
        RemoveConnections(x => x.SessionKey.ProjectId == projectId);

    public IReadOnlyList<PlanningPokerConnection> RemoveOrganization(int organizationId) =>
        RemoveConnections(x => x.SessionKey.OrganizationId == organizationId);

    private IReadOnlyList<PlanningPokerConnection> RemoveConnections(
        Func<PlanningPokerConnection, bool> predicate)
    {
        lock (_lock)
        {
            var matches = _connections.Values.Where(predicate).ToArray();
            foreach (var connection in matches)
            {
                _connections.Remove(connection.ConnectionId);
                if (_sessions.TryGetValue(connection.SessionKey, out var session))
                {
                    session.Participants.Remove(connection.ConnectionId);
                }
            }
            return matches;
        }
    }

    public PlanningPokerSessionKey? GetSessionKey(string connectionId)
    {
        lock (_lock)
            return _connections.TryGetValue(connectionId, out var connection)
                ? connection.SessionKey
                : null;
    }

    public void SetVote(string connectionId, int? points)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(connectionId, out var connection)) return;
            var sessionKey = connection.SessionKey;
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
            if (!_connections.TryGetValue(connectionId, out var connection)) return;
            var sessionKey = connection.SessionKey;
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
            if (!_connections.TryGetValue(connectionId, out var connection)) return null;
            var sessionKey = connection.SessionKey;
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
            if (!_connections.TryGetValue(connectionId, out var connection)) return;
            var sessionKey = connection.SessionKey;
            GetOrCreateSession(sessionKey).Revealed = true;
        }
    }

    public void Reset(string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(connectionId, out var connection)) return;
            var sessionKey = connection.SessionKey;
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
            if (!_connections.TryGetValue(connectionId, out var connection)) return null;
            var sessionKey = connection.SessionKey;
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
