using System;

namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Indicates the relative priority of a PBI in the backlog.
    /// </summary>
    public enum PbiPriority
    {
        /// <summary>No priority has been assigned.</summary>
        None,
        /// <summary>Low-urgency item; address after higher priorities.</summary>
        Low,
        /// <summary>Standard priority for routine backlog items.</summary>
        Medium,
        /// <summary>Urgent item that should be addressed in the current or next sprint.</summary>
        High
    }
}