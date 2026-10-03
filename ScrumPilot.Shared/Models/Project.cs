namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Represents a Scrum project that owns epics, sprints, and backlog items.
    /// </summary>
    public class Project
    {
        /// <summary>Unique identifier for this project.</summary>
        public int ProjectId { get; set; }

        /// <summary>Identifier of the organization that owns this project.</summary>
        public int OrganizationId { get; set; }

        /// <summary>Display name of the project.</summary>
        public required string ProjectName { get; set; }

        /// <summary>Optional description of the project's purpose.</summary>
        public string? Description { get; set; }

        /// <summary>Epics that belong to this project.</summary>
        public ICollection<Epic>? Epics { get; set; }

        /// <summary>Sprints that belong to this project.</summary>
        public ICollection<Sprint>? Sprints { get; set; }

        /// <summary>All PBIs that belong to this project.</summary>
        public ICollection<ProductBacklogItem>? ProductBacklogItems { get; set; }

        /// <summary>Organization that owns this project.</summary>
        public Organization? Organization { get; set; }
    }
}
