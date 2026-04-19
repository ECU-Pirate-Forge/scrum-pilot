namespace ScrumPilot.Shared.Models
{
    public class Epic
    {
        public int EpicId { get; set; }
        public int ProjectId { get; set; }
        public required string Name { get; set; }
        public DateTime DateCreated { get; set; }
        public ICollection<ProductBacklogItem>? ProductBacklogItems { get; set; }
    }
}
