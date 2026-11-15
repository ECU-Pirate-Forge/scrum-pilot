namespace ScrumPilot.Shared.Models;

/// <summary>Append-only Owner acknowledgment that unchanged story content applies to an exact current input fingerprint.</summary>
public sealed class PlanningArtifactReview
{
    public int PlanningArtifactReviewId { get; set; }
    public int ProjectId { get; set; }
    public int PbiId { get; set; }
    public required string Fingerprint { get; set; }
    public required string ActorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public required string Reason { get; set; }
}
