namespace ScrumPilot.Shared.Models;

public enum GenerationKind { Stories = 0, Tasks = 1 }
public enum GenerationState { Pending = 0, Running = 1, Succeeded = 2, Failed = 3, Cancelled = 4 }
public enum TaskScopeMode { Advisory = 0, Enforced = 1 }
public enum SpecificationApproval { Draft = 0, Approved = 1, Rejected = 2 }
public enum AllowedFileCategory { Source = 0, Test = 1, Configuration = 2, Documentation = 3, Other = 4 }
public enum AllowedFileOperation { Add = 0, Modify = 1, Delete = 2, Rename = 3 }
public enum ScopeCheckState { Pending = 0, Passed = 1, Failed = 2, Error = 3 }

public class ProjectGuidelinesRevision
{
    public int ProjectGuidelinesRevisionId { get; set; }
    public int ProjectId { get; set; }
    public int Revision { get; set; }
    public required string Content { get; set; }
    public string ContentHash { get; set; } = "";
    public required string ActorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ProblemStatement
{
    public int ProblemStatementId { get; set; }
    public int ProjectId { get; set; }
    public required string Title { get; set; }
    public required string Text { get; set; }
    public bool IsArchived { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GenerationRun
{
    public int GenerationRunId { get; set; }
    public int ProjectId { get; set; }
    public GenerationKind Kind { get; set; }
    public required string Provider { get; set; }
    public string? Model { get; set; }
    public required string IdempotencyKey { get; set; }
    public GenerationState State { get; set; }
    public required string ActorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
    public long Version { get; set; }
}

/// <summary>Immutable exact input snapshot; no credentials or provider secrets.</summary>
public class GenerationInput
{
    public int GenerationInputId { get; set; }
    public int ProjectId { get; set; }
    public int GenerationRunId { get; set; }
    public int? ProjectGuidelinesRevisionId { get; set; }
    public int? ProblemStatementId { get; set; }
    public int? RepositorySnapshotId { get; set; }
    public required string SnapshotJson { get; set; }
    public string ContentHash { get; set; } = "";
}

public class ProjectRepository
{
    public int ProjectRepositoryId { get; set; }
    public int ProjectId { get; set; }
    public long GitHubRepositoryId { get; set; }
    public long InstallationId { get; set; }
    public required string Owner { get; set; }
    public required string Name { get; set; }
    public required string DefaultBranch { get; set; }
    public long Version { get; set; }
}

public class RepositorySnapshot
{
    public int RepositorySnapshotId { get; set; }
    public int ProjectId { get; set; }
    public int ProjectRepositoryId { get; set; }
    public required string CommitSha { get; set; }
    public required string TreeSha { get; set; }
    public required string TreeJson { get; set; }
    public string Branch { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Versioned JSON payload. Array order is significant when computing the content hash.</summary>
public sealed record TaskSpecificationContent
{
    public int SchemaVersion { get; init; } = 1;
    public string Goal { get; init; } = "";
    public string[] Requirements { get; init; } = [];
    public string[] AcceptanceCriteria { get; init; } = [];
    public string[] VerificationSteps { get; init; } = [];
    public string[] OutOfScope { get; init; } = [];
    public string CurrentContext { get; init; } = "";
    public string[] ImplementationSteps { get; init; } = [];
    public string[] Documentation { get; init; } = [];
    public string[] DefinitionOfDone { get; init; } = [];
    public long SourceStoryVersion { get; init; }
    public string SourceStorySnapshot { get; init; } = "";
}

/// <summary>Entire revision, including approval and scope, is immutable; changes create a new revision.</summary>
public class TaskSpecificationRevision
{
    public int TaskSpecificationRevisionId { get; set; }
    public int ProjectId { get; set; }
    public int PbiId { get; set; }
    public int Revision { get; set; }
    public TaskSpecificationContent Content { get; set; } = new();
    public TaskScopeMode ScopeMode { get; set; }
    public SpecificationApproval Approval { get; set; }
    public string? ApprovedByActorId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string ContentHash { get; set; } = "";
    public int? GenerationRunId { get; set; }
    public int? ProjectGuidelinesRevisionId { get; set; }
    public int? RepositorySnapshotId { get; set; }
    public required string ActorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<TaskAllowedFile> AllowedFiles { get; set; } = [];
}

public class TaskAllowedFile
{
    public int TaskAllowedFileId { get; set; }
    public int ProjectId { get; set; }
    public int TaskSpecificationRevisionId { get; set; }
    public required string Path { get; set; }
    public AllowedFileCategory Category { get; set; }
    public AllowedFileOperation Operation { get; set; }
    public string? RenameFromPath { get; set; }
    public required string Rationale { get; set; }
}

public class PbiDependency
{
    public string ActorId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int ProjectId { get; set; }
    public int DependentPbiId { get; set; }
    public int PrerequisitePbiId { get; set; }
}

/// <summary>Review is current only when ReviewedRevision equals InvalidationRevision.</summary>
public class DependencyReview
{
    public int ProjectId { get; set; }
    public int PbiId { get; set; }
    public long InvalidationRevision { get; set; }
    public long? ReviewedRevision { get; set; }
    public string? ReviewedByActorId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public long Version { get; set; }
}

public class DependencyReviewEvent
{
    public int DependencyReviewEventId { get; set; }
    public int ProjectId { get; set; }
    public int PbiId { get; set; }
    public long InvalidationRevision { get; set; }
    public bool Accepted { get; set; }
    public string ActorId { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>API explicitly touches this row in each graph mutation transaction; SaveChanges increments Revision.</summary>
public class ProjectGraphState
{
    public int ProjectId { get; set; }
    public long Revision { get; set; }
}

public class TaskPullRequestBinding
{
    public int TaskPullRequestBindingId { get; set; }
    public int ProjectId { get; set; }
    public int PbiId { get; set; }
    public int ProjectRepositoryId { get; set; }
    public int PullRequestNumber { get; set; }
    public string ActorId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PullRequestScopeCheck
{
    public int PullRequestScopeCheckId { get; set; }
    public int ProjectId { get; set; }
    public int TaskPullRequestBindingId { get; set; }
    public int TaskSpecificationRevisionId { get; set; }
    public required string HeadSha { get; set; }
    public string BaseSha { get; set; } = "";
    public string MergeBaseSha { get; set; } = "";
    public long? GitHubCheckId { get; set; }
    public ScopeCheckState State { get; set; }
    public required string ResultJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class NonTaskPullRequestException
{
    public int NonTaskPullRequestExceptionId { get; set; }
    public int ProjectId { get; set; }
    public int ProjectRepositoryId { get; set; }
    public int PullRequestNumber { get; set; }
    public required string Reason { get; set; }
    public required string ActorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GitHubWebhookDelivery
{
    public required string DeliveryId { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
