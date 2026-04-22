namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Represents a time-boxed Sprint within a project.
    /// </summary>
    public class Sprint
    {
        /// <summary>Unique identifier for this sprint.</summary>
        public int SprintId { get; set; }

        /// <summary>The project this sprint belongs to.</summary>
        public int ProjectId { get; set; }

        /// <summary>Optional display name for the sprint (e.g., "Sprint 3").</summary>
        public string? SprintTitle { get; set; }

        /// <summary>The team's stated goal for the sprint.</summary>
        public string? SprintGoal { get; set; }

        /// <summary>UTC date the sprint begins.</summary>
        public DateTime? StartDate { get; set; }

        /// <summary>UTC date the sprint is scheduled to end.</summary>
        public DateTime? EndDate { get; set; }

        /// <summary>Indicates whether the sprint is currently active.</summary>
        public bool IsOpen { get; set; }

        /// <summary>UTC timestamp when the sprint was closed, or <c>null</c> if still open.</summary>
        public DateTime? DateClosed { get; set; }

        /// <summary>PBIs assigned to this sprint.</summary>
        public ICollection<ProductBacklogItem>? ProductBacklogItems { get; set; }
    }
}
