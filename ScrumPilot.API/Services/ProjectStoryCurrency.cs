using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>DB-only currency evaluation, safe to call from graph and specification services without a DI cycle.</summary>
public static class ProjectStoryCurrency
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<PlanningArtifactCurrency> ReadAsync(
        ScrumPilotContext db, int projectId, int storyId, CancellationToken ct = default)
    {
        var currencies = await ReadManyAsync(db, projectId, [storyId], ct);
        return currencies.TryGetValue(storyId, out var currency) ? currency : throw new KeyNotFoundException();
    }

    /// <summary>At most seven set-based reads, independent of item count. Missing or foreign-project IDs are omitted.</summary>
    public static async Task<IReadOnlyDictionary<int, PlanningArtifactCurrency>> ReadManyAsync(
        ScrumPilotContext db, int projectId, IReadOnlyCollection<int> storyIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(storyIds);
        ct.ThrowIfCancellationRequested();
        if (storyIds.Count == 0) return new Dictionary<int, PlanningArtifactCurrency>();
        var ids = storyIds.Distinct().ToArray();
        var stories = await db.Stories.AsNoTracking().Where(p => p.ProjectId == projectId && ids.Contains(p.PbiId))
            .Select(p => new { p.PbiId, p.Type, p.Title, p.Description, p.ProblemStatementId, p.GenerationRunId, p.GenerationCandidateKey })
            .ToListAsync(ct);
        if (stories.Count == 0) return new Dictionary<int, PlanningArtifactCurrency>();
        var sourceIds = stories.Where(p => p.ProblemStatementId.HasValue).Select(p => p.ProblemStatementId!.Value).Distinct().ToArray();
        var sources = await db.ProblemStatements.AsNoTracking().Where(p => p.ProjectId == projectId &&
            sourceIds.Contains(p.ProblemStatementId)).Select(p => new SourceState(p.ProblemStatementId, p.Version, p.IsArchived))
            .ToDictionaryAsync(p => p.Id, ct);
        var runIds = stories.Where(p => p.GenerationRunId.HasValue).Select(p => p.GenerationRunId!.Value).Distinct().ToArray();
        var inputs = await db.GenerationInputs.AsNoTracking().Where(i => i.ProjectId == projectId &&
            runIds.Contains(i.GenerationRunId)).Select(i =>
                new InputState(i.GenerationRunId, i.ProblemStatementId, i.ProjectGuidelinesRevisionId, i.SnapshotJson))
            .ToDictionaryAsync(i => i.RunId, ct);
        // Parse each shared run's snapshot once, not once for every generated story.
        var snapshots = inputs.ToDictionary(pair => pair.Key, pair => ReadInput(pair.Value.Json));
        var currentRevision = await db.Projects.AsNoTracking().Where(p => p.ProjectId == projectId)
            .Select(p => p.CurrentGuidelinesRevisionId).SingleAsync(ct);
        var pendingReviews = await db.DependencyReviews.AsNoTracking().Where(r => r.ProjectId == projectId &&
            ids.Contains(r.PbiId) && r.ReviewedRevision != r.InvalidationRevision).Select(r => r.PbiId).ToHashSetAsync(ct);
        var fingerprints = stories.ToDictionary(p => p.PbiId, p => Fingerprint(projectId, p.PbiId, p.Type, p.Title,
            p.Description, p.ProblemStatementId, p.GenerationRunId, p.GenerationCandidateKey,
            p.ProblemStatementId is int sourceId ? sources.GetValueOrDefault(sourceId) : null, currentRevision));
        var latestReviews = await db.PlanningArtifactReviews.AsNoTracking().Where(r => r.ProjectId == projectId &&
            ids.Contains(r.PbiId)).GroupBy(r => r.PbiId)
            .Select(g => g.OrderByDescending(r => r.PlanningArtifactReviewId).First()).ToDictionaryAsync(r => r.PbiId, ct);
        var matchingReviews = new Dictionary<int, PlanningArtifactReview>();
        if (latestReviews.Count > 0)
        {
            var hashes = fingerprints.Values.Distinct().ToArray();
            matchingReviews = await db.PlanningArtifactReviews.AsNoTracking().Where(r => r.ProjectId == projectId &&
                ids.Contains(r.PbiId) && hashes.Contains(r.Fingerprint)).GroupBy(r => r.PbiId)
                .Select(g => g.OrderByDescending(r => r.PlanningArtifactReviewId).First()).ToDictionaryAsync(r => r.PbiId, ct);
        }
        var results = new Dictionary<int, PlanningArtifactCurrency>(stories.Count);
        foreach (var story in stories)
        {
            var source = story.ProblemStatementId is int sourceId ? sources.GetValueOrDefault(sourceId) : null;
            var input = story.GenerationRunId is int runId ? inputs.GetValueOrDefault(runId) : null;
            var snapshot = input is null ? null : snapshots[input.RunId];
            var currency = Evaluate(story.ProblemStatementId, source, input, snapshot, currentRevision, pendingReviews.Contains(story.PbiId));
            var matching = matchingReviews.GetValueOrDefault(story.PbiId);
            var latest = latestReviews.GetValueOrDefault(story.PbiId);
            var current = matching?.Fingerprint == fingerprints[story.PbiId];
            results.Add(story.PbiId, currency with
            {
                CurrentFingerprint = fingerprints[story.PbiId],
                ApplicabilityReviewed = current,
                ApplicabilityReview = matching ?? latest,
                ContentChanged = latest is not null && !current,
                SourceChanged = !current && currency.SourceChanged,
                GuidelinesChanged = !current && currency.GuidelinesChanged,
                SourceArchived = !current && currency.SourceArchived
                // DependencyReviewRequired deliberately remains exactly as read from dependency review state.
            });
        }
        return results;
    }

    internal static async Task<PlanningArtifactCurrency> FromInputAsync(
        ScrumPilotContext db, int projectId, int? sourceId, GenerationInput? input, CancellationToken ct)
    {
        var source = sourceId.HasValue ? await db.ProblemStatements.AsNoTracking().Where(p =>
            p.ProjectId == projectId && p.ProblemStatementId == sourceId)
            .Select(p => new SourceState(p.ProblemStatementId, p.Version, p.IsArchived)).SingleOrDefaultAsync(ct) : null;
        var snapshot = input is null ? null : ReadInput(input.SnapshotJson);
        var currentRevision = await db.Projects.AsNoTracking().Where(p => p.ProjectId == projectId)
            .Select(p => p.CurrentGuidelinesRevisionId).SingleAsync(ct);
        var inputState = input is null ? null : new InputState(input.GenerationRunId, input.ProblemStatementId,
            input.ProjectGuidelinesRevisionId, input.SnapshotJson);
        return Evaluate(sourceId, source, inputState, snapshot, currentRevision, false);
    }

    private static PlanningArtifactCurrency Evaluate(int? sourceId, SourceState? source, InputState? input,
        SnapshotSource? snapshot, int? currentRevision, bool dependencyReviewRequired)
    {
        var hasSnapshot = snapshot is not null && input?.ProblemStatementId == sourceId &&
            snapshot.ProblemStatementId == sourceId;
        return new(sourceId.HasValue, hasSnapshot,
            snapshot is not null && (!hasSnapshot || source?.Version != snapshot.ProblemVersion),
            input is not null && input.GuidelinesRevisionId != currentRevision,
            source?.IsArchived ?? false, dependencyReviewRequired, input?.GuidelinesRevisionId)
        { CurrentSourceArchived = source?.IsArchived ?? false };
    }

    private static string Fingerprint(int projectId, int storyId, PbiType type, string title, string description,
        int? sourceId, int? runId, string? candidateKey, SourceState? source, int? currentGuidelinesRevisionId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            ProjectId = projectId,
            StoryId = storyId,
            Type = type,
            Title = title,
            Description = description,
            GenerationRunId = runId,
            GenerationCandidateKey = candidateKey,
            GuidelinesRevisionId = currentGuidelinesRevisionId,
            ProblemStatementId = sourceId,
            ProblemVersion = source?.Version,
            ProblemIsArchived = source?.IsArchived,
            ProblemMissing = sourceId.HasValue && source is null
        }, JsonOptions))));

    private static SnapshotSource? ReadInput(string json)
    {
        using var document = JsonDocument.Parse(json);
        // Also recognize the original input-only format for previously persisted story lineage.
        var root = document.RootElement;
        if (root.TryGetProperty("input", out var input)) root = input;
        var snapshot = root.Deserialize<SnapshotSource>(JsonOptions);
        return snapshot?.ProblemStatementId > 0 && snapshot.ProblemVersion > 0 ? snapshot : null;
    }

    private sealed record SourceState(int Id, long Version, bool IsArchived);
    private sealed record InputState(int RunId, int? ProblemStatementId, int? GuidelinesRevisionId, string Json);
    private sealed record SnapshotSource(int ProblemStatementId, long ProblemVersion);
}

internal sealed record ProjectStoryInputSnapshot(int ProblemStatementId, long ProblemVersion, string Title, string Text,
    int? GuidelinesRevisionId, string? Guidelines);
internal sealed record StoredStoryProposal(int ProposalId, string CandidateKey, ProjectStoryAiContent Content);
internal sealed record StoredStorySnapshot(ProjectStoryInputSnapshot Input, IReadOnlyList<StoredStoryProposal> Proposals, int SchemaVersion = 1);
