namespace ScrumPilot.Shared.Models;

/// <summary>
/// Records a single status transition for a PBI, used to power burndown and cycle-time metrics.
/// </summary>
public class PbiStatusHistory
{
    /// <summary>Unique identifier for this history record.</summary>
    public int Id { get; set; }

    /// <summary>The PBI whose status changed.</summary>
    public int PbiId { get; set; }

    /// <summary>The status the PBI transitioned from.</summary>
    public PbiStatus FromStatus { get; set; }

    /// <summary>The status the PBI transitioned to.</summary>
    public PbiStatus ToStatus { get; set; }

    /// <summary>UTC timestamp when the transition occurred.</summary>
    public DateTime ChangedAt { get; set; }
}
