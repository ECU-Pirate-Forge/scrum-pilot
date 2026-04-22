namespace ScrumPilot.Shared.Models.PlanningPoker;

/// <summary>
/// Represents a single participant in an active planning poker session.
/// </summary>
public class ParticipantState
{
    /// <summary>SignalR connection ID uniquely identifying this participant's session.</summary>
    public string ConnectionId { get; set; } = "";

    /// <summary>The name the participant chose when joining the session.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Indicates whether this participant has cast their vote.</summary>
    public bool HasVoted { get; set; }

    /// <summary>The participant's vote in story points, or <c>null</c> until votes are revealed.</summary>
    public int? Points { get; set; }
}
