namespace ScrumPilot.Shared.Models
{
    public class ProductBacklogItem
    {
        public int PbiId { get; set; }
        public PbiType Type { get; set; }
        public int? EpicId { get; set; }
        public int? SprintId { get; set; }
        public required string Title { get; set; }
        public string Description { get; set; } = "";
        public PbiStatus Status { get; set; }
        public PbiPriority Priority { get; set; }
        public PbiPoints StoryPoints { get; set; }
        public PbiOrigin Origin { get; set; }
        public bool IsDraft { get; set; }
        public bool IsFlagged { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}