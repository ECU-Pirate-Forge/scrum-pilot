namespace ScrumPilot.Shared.Models
{
    public class Sprint
    {
        public int SprintId { get; set; }
        public int ProjectId { get; set; }
        public string? SprintTitle { get; set; }
        public string? SprintGoal { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool IsOpen { get; set; }
        public DateTime? DateClosed { get; set; }
        public ICollection<ProductBacklogItem>? ProductBacklogItems { get; set; }
    }
}
