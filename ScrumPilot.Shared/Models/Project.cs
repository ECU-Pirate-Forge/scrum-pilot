namespace ScrumPilot.Shared.Models
{
    public class Project
    {
        public int ProjectId { get; set; }
        public required string ProjectName { get; set; }
        public string? Description { get; set; }
        public ICollection<Epic>? Epics { get; set; }
        public ICollection<Sprint>? Sprints { get; set; }
        public ICollection<ProductBacklogItem>? ProductBacklogItems { get; set; }
    }
}
