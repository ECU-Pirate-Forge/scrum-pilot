namespace ScrumPilot.Shared.Models.PlanningPoker;

public class PokerSessionState
{
    public int? CurrentPbiId { get; set; }
    public bool Revealed { get; set; }
    public List<ParticipantState> Participants { get; set; } = [];
}
