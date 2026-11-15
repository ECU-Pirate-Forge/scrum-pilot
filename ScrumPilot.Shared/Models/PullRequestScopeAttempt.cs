namespace ScrumPilot.Shared.Models;

public sealed class PullRequestScopeAttempt
{
    public int PullRequestScopeAttemptId { get; set; }
    public int ProjectId { get; set; }
    public int ProjectRepositoryId { get; set; }
    public int PullRequestNumber { get; set; }
    public string HeadSha { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
