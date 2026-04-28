using ScrumPilot.Shared.Models.PlanningPoker;

namespace ScrumPilot.API.Services;

public class PlanningPokerSessionService
{
    private sealed class ProjectSession
    {
        public Dictionary<string, (string DisplayName, int? Points, bool HasVoted)> Participants = [];
        public int? CurrentPbiId;
        public bool Revealed;
    }

    private readonly Dictionary<int, ProjectSession> _sessions = [];
    private readonly Dictionary<string, int> _connectionToProject = [];
    private readonly object _lock = new();

    private ProjectSession GetOrCreateSession(int projectId)
    {
        if (!_sessions.TryGetValue(projectId, out var session))
        {
            session = new ProjectSession();
            _sessions[projectId] = session;
        }
        return session;
    }

    public void AddParticipant(string connectionId, string displayName, int projectId)
    {
        lock (_lock)
        {
            _connectionToProject[connectionId] = projectId;
            GetOrCreateSession(projectId).Participants[connectionId] = (displayName, null, false);
        }
    }

    public int? RemoveParticipant(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionToProject.TryGetValue(connectionId, out var projectId))
                return null;
            _connectionToProject.Remove(connectionId);
            if (_sessions.TryGetValue(projectId, out var session))
                session.Participants.Remove(connectionId);
            return projectId;
        }
    }

    public int? GetProjectId(string connectionId)
    {
        lock (_lock)
            return _connectionToProject.TryGetValue(connectionId, out var id) ? id : null;
    }

    public void SetVote(string connectionId, int? points)
    {
        lock (_lock)
        {
            if (!_connectionToProject.TryGetValue(connectionId, out var projectId)) return;
            var s = GetOrCreateSession(projectId);
            if (s.Participants.ContainsKey(connectionId))
                s.Participants[connectionId] = (s.Participants[connectionId].DisplayName, points, true);
        }
    }

    public void SetCurrentPbi(string connectionId, int? pbiId)
    {
        lock (_lock)
        {
            if (!_connectionToProject.TryGetValue(connectionId, out var projectId)) return;
            var s = GetOrCreateSession(projectId);
            s.CurrentPbiId = pbiId;
            s.Revealed = false;
            foreach (var key in s.Participants.Keys.ToList())
                s.Participants[key] = (s.Participants[key].DisplayName, null, false);
        }
    }

    public void Reveal(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionToProject.TryGetValue(connectionId, out var projectId)) return;
            GetOrCreateSession(projectId).Revealed = true;
        }
    }

    public void Reset(string connectionId)
    {
        lock (_lock)
        {
            if (!_connectionToProject.TryGetValue(connectionId, out var projectId)) return;
            var s = GetOrCreateSession(projectId);
            s.Revealed = false;
            foreach (var key in s.Participants.Keys.ToList())
                s.Participants[key] = (s.Participants[key].DisplayName, null, false);
        }
    }

    public PokerSessionState? GetState(string connectionId, bool includeVotes = false)
    {
        lock (_lock)
        {
            if (!_connectionToProject.TryGetValue(connectionId, out var projectId)) return null;
            return GetStateForProject(projectId, includeVotes);
        }
    }

    public PokerSessionState GetStateForProject(int projectId, bool includeVotes = false)
    {
        lock (_lock)
        {
            var s = GetOrCreateSession(projectId);
            var showVotes = includeVotes || s.Revealed;
            return new PokerSessionState
            {
                CurrentPbiId = s.CurrentPbiId,
                Revealed = s.Revealed,
                Participants = s.Participants.Select(kvp => new ParticipantState
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
