namespace ScrumPilot.Shared.Models.PlanningPoker;

/// <summary>
/// Snapshot of the current planning poker session broadcast to all participants via SignalR.
/// </summary>
public class PokerSessionState
{
    /// <summary>The PBI currently being estimated, or <c>null</c> if no item is selected.</summary>
    public int? CurrentPbiId { get; set; }

    /// <summary>Indicates whether votes have been revealed to all participants.</summary>
    public bool Revealed { get; set; }

    /// <summary>All participants currently connected to the session.</summary>
    public List<ParticipantState> Participants { get; set; } = [];
}
