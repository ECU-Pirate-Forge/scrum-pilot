namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Represents a Product Backlog Item (PBI) — the core unit of work tracked on the Scrum board.
    /// </summary>
    public class ProductBacklogItem
    {
        /// <summary>Unique identifier for this PBI.</summary>
        public int PbiId { get; set; }

        /// <summary>The project this PBI belongs to.</summary>
        public int ProjectId { get; set; }

        /// <summary>The type of work item (Story, Bug, or Task).</summary>
        public PbiType Type { get; set; }

        /// <summary>Optional epic this PBI is grouped under.</summary>
        public int? EpicId { get; set; }

        /// <summary>Optional sprint this PBI is assigned to.</summary>
        public int? SprintId { get; set; }

        /// <summary>Short, descriptive title of the backlog item.</summary>
        public required string Title { get; set; }

        /// <summary>Full description including the user story and acceptance criteria.</summary>
        public string Description { get; set; } = "";

        /// <summary>Current workflow status of the PBI on the Scrum board.</summary>
        public PbiStatus Status { get; set; }

        /// <summary>Priority level used to order the backlog.</summary>
        public PbiPriority Priority { get; set; }

        /// <summary>Fibonacci story point estimate for this PBI.</summary>
        public PbiPoints StoryPoints { get; set; }

        /// <summary>Indicates how this PBI was created (AI, bot, or manual).</summary>
        public PbiOrigin Origin { get; set; }

        /// <summary>When <c>true</c>, this PBI is in draft state and not yet committed to the backlog.</summary>
        public bool IsDraft { get; set; }

        /// <summary>When <c>true</c>, this PBI has been flagged to draw team attention.</summary>
        public bool IsFlagged { get; set; }

        /// <summary>Identity ID of the team member assigned to this PBI, or <c>null</c> if unassigned.</summary>
        public string? AssignedToUserId { get; set; }

        /// <summary>Optional URL linking this PBI to an external issue tracker.</summary>
        public string? IssueLink { get; set; }

        /// <summary>PBI ID this item depends on, used to build the dependency graph.</summary>
        public int? DependsOnPbiId { get; set; }

        /// <summary>UTC timestamp when this PBI was created.</summary>
        public DateTime DateCreated { get; set; }

        /// <summary>UTC timestamp of the most recent update to this PBI.</summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>Comments left by team members on this PBI.</summary>
        public ICollection<Comment>? Comments { get; set; }
    }
}