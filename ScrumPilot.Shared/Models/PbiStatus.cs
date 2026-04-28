namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Represents the workflow status of a PBI on the Scrum board.
    /// </summary>
    public enum PbiStatus
    {
        /// <summary>Work has not yet started.</summary>
        ToDo,
        /// <summary>Work is actively being developed.</summary>
        InProgress,
        /// <summary>Work is awaiting peer or QA review.</summary>
        InReview,
        /// <summary>Work is complete and accepted.</summary>
        Done
    }
}
