namespace ScrumPilot.Shared.Models.PlanningPoker;

public class ParticipantState
{
    public string ConnectionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool HasVoted { get; set; }
    public int? Points { get; set; }
}
