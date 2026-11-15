namespace ScrumPilot.Shared.Models;

public sealed record SaveProjectGuidelinesRequest(string Content, int? ExpectedRevisionId);
public sealed record RestoreProjectGuidelinesRequest(int? ExpectedRevisionId);
public sealed record SaveProblemStatementRequest(string Title, string Text, long? ExpectedVersion, bool IsArchived);
public sealed record GenerateProjectStoriesRequest(string IdempotencyKey, IReadOnlyList<int> ProblemStatementIds);
public sealed record ProjectStoryBatchResult(IReadOnlyList<ProjectStoryRunResult> Results);
public sealed record ProjectStoryRunResult(int ProblemStatementId, int? GenerationRunId, GenerationState State,
    string? ErrorCode, IReadOnlyList<StoryProposalDto> Proposals);
/// <summary>ProposalId identifies an immutable server-owned candidate within its run, not a PBI. No PBI exists until acceptance.</summary>
public sealed record StoryProposalDto(int ProposalId, int ProblemStatementId, int GenerationRunId,
    ProductBacklogItem Pbi, PlanningArtifactCurrency Currency, int? AcceptedPbiId = null);
public sealed record AcceptProjectStoriesRequest(int GenerationRunId, bool AsDraft, IReadOnlyList<AcceptStoryProposal> Items);
public sealed record AcceptStoryProposal(int ProposalId, long ExpectedVersion, string Title, string Description,
    PbiPriority Priority, PbiPoints StoryPoints);
public sealed record LinkLegacyStoryRequest(int ProblemStatementId, long ExpectedVersion);
public sealed record ReviewStoryApplicabilityRequest(string ExpectedFingerprint, string Reason);
public sealed record PlanningArtifactCurrency(bool HasSource, bool HasGenerationSnapshot, bool SourceChanged,
    bool GuidelinesChanged, bool SourceArchived, bool DependencyReviewRequired, int? GuidelinesRevisionId)
{
    public string CurrentFingerprint { get; init; } = "";
    public bool ContentChanged { get; init; }
    public bool ApplicabilityReviewed { get; init; }
    public PlanningArtifactReview? ApplicabilityReview { get; init; }
    public bool CurrentSourceArchived { get; init; }
    public bool RequiresReview => (!ApplicabilityReviewed && HasSource && !HasGenerationSnapshot) ||
        ContentChanged || SourceChanged || GuidelinesChanged || SourceArchived || DependencyReviewRequired;
}
/// <summary>Local AI output contains content only. Source, project, run and proposal identities are assigned by the server.</summary>
public sealed record ProjectStoryAiOutput(IReadOnlyList<ProjectStoryAiContent> Stories);
public sealed record ProjectStoryAiContent(string Title, string Description, PbiPriority Priority, PbiPoints StoryPoints);
