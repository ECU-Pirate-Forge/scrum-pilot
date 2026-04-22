namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Classifies the nature of a PBI for reporting and filtering.
    /// </summary>
    public enum PbiType
    {
        /// <summary>A user-facing feature or capability.</summary>
        Story,
        /// <summary>A defect or unintended behaviour to be fixed.</summary>
        Bug,
        /// <summary>A technical or infrastructure task with no direct user-facing value.</summary>
        Task
    }
}
