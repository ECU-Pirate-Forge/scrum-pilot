namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Represents a large body of work (Epic) that groups related PBIs.
    /// </summary>
    public class Epic
    {
        /// <summary>Unique identifier for this epic.</summary>
        public int EpicId { get; set; }

        /// <summary>The project this epic belongs to.</summary>
        public int ProjectId { get; set; }

        /// <summary>Display name of the epic.</summary>
        public required string Name { get; set; }

        /// <summary>UTC timestamp when this epic was created.</summary>
        public DateTime DateCreated { get; set; }

        /// <summary>PBIs grouped under this epic.</summary>
        public ICollection<ProductBacklogItem>? ProductBacklogItems { get; set; }
    }
}
