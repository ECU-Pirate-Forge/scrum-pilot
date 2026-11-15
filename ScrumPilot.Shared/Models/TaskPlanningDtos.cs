namespace ScrumPilot.Shared.Models;

public sealed record TaskFileProposal(string Path, AllowedFileCategory Category, AllowedFileOperation Operation,
    string Rationale, string? RenameFromPath = null);
public sealed record TaskProposalDto(string Key, int ParentStoryId, string Title,
    TaskSpecificationContent Content, TaskFileProposal[] Files, string[] DependsOnKeys, int[] ExistingPrerequisites);
public sealed record TaskGenerationOutput(TaskProposalDto[] Tasks);
public sealed record GenerateTaskPreviewRequest(int[] StoryIds, int RepositorySnapshotId, string[] SourcePaths, string IdempotencyKey);
public sealed record TaskGenerationPreviewDto(int GenerationRunId, int RepositorySnapshotId, int? GuidelinesRevisionId,
    TaskProposalDto[] Tasks);
public sealed record AcceptTaskProposalsRequest(int GenerationRunId, TaskProposalDto[] Tasks, bool AsDraft);
public sealed record SaveTaskSpecificationRequest(TaskSpecificationContent Content, TaskFileProposal[] Files,
    int RepositorySnapshotId, int ExpectedRevision);
public sealed record ApproveTaskSpecificationRequest(int ExpectedRevision);
public sealed record TaskSpecificationStatusDto(TaskSpecificationRevision? Specification, bool IsCurrent,
    string[] ReviewReasons);
public sealed record BindTaskPullRequestRequest(int PullRequestNumber);
