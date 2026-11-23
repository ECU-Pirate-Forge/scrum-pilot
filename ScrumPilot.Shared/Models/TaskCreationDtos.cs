namespace ScrumPilot.Shared.Models;

public sealed record TaskCreationAiTask(
    string Key,
    int ParentPbiId,
    string Title,
    string Description,
    string[] DependsOnKeys,
    int[] ExistingPrerequisites);

public sealed record TaskCreationAiOutput(TaskCreationAiTask[] Tasks);

public sealed record GenerateTaskCreationPreviewRequest(
    int[] ParentPbiIds,
    string IdempotencyKey,
    bool ReplaceGeneratedTasks = false);

public sealed record TaskCreationProposalDto(
    string Key,
    int ParentPbiId,
    string Title,
    string Description,
    string[] DependsOnKeys,
    int[] ExistingPrerequisites,
    long SourceVersion,
    string SourceSnapshot);

public sealed record TaskCreationPreviewDto(
    int GenerationRunId,
    // int? GuidelinesRevisionId,
    TaskCreationProposalDto[] Tasks);

public sealed record AcceptTaskCreationProposalsRequest(
    int GenerationRunId,
    TaskCreationProposalDto[] Tasks,
    bool AsDraft = true);

public sealed record CreateManualChildTaskRequest(int ParentPbiId, string Title, string Description);
