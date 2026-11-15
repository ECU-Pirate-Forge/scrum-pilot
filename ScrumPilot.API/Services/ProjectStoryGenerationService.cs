using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

/// <summary>Previews are immutable generation snapshots, not PBIs. Only accepted candidates create work.</summary>
public sealed class ProjectStoryGenerationService(ScrumPilotContext db, LocalPlanningAi ai,
    IPlanningMutationHandler planning, IConfiguration? configuration = null, PullRequestScopeService? scopes = null,
    ProjectWorkNotifier? notifier = null)
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private const string Instructions = """
        Generate one or more Scrum user stories for this problem using the supplied project guidelines.
        Return exactly {"stories":[{"title":"short title","description":"As a role, I want a goal, so that a benefit.\nAcceptance criteria:\n- observable outcome","priority":"Medium","storyPoints":"Zero"}]}.
        Priority is None, Low, Medium or High. StoryPoints is Zero, One, Two, Three, Five, Eight, Thirteen or TwentyOne.
        Each story must have a nonempty title (maximum 200 characters) and description (maximum 2000 characters including acceptance criteria).
        Do not output identifiers, tasks or extra properties.
        """;

    public Task<List<ProblemStatement>> ProblemsAsync(int projectId, CancellationToken ct) =>
        db.ProblemStatements.AsNoTracking().Where(p => p.ProjectId == projectId)
            .OrderBy(p => p.ProblemStatementId).ToListAsync(ct);

    public async Task<ProblemStatement> SaveProblemAsync(int projectId, int? id,
        SaveProblemStatementRequest request, CancellationToken ct)
    {
        ValidateText(request.Title, request.Text);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        ProblemStatement problem;
        if (id.HasValue)
        {
            problem = await db.ProblemStatements.SingleOrDefaultAsync(p => p.ProjectId == projectId &&
                p.ProblemStatementId == id, ct) ?? throw new KeyNotFoundException();
            if (request.ExpectedVersion != problem.Version) throw new DbUpdateConcurrencyException();
            if (problem.Title != request.Title.Trim() || problem.Text != request.Text || problem.IsArchived != request.IsArchived)
            {
                var stories = db.Stories.Where(p => p.ProjectId == projectId && p.ProblemStatementId == id && p.Type == PbiType.Story)
                    .Select(p => p.PbiId);
                var tasks = await db.Stories.AsNoTracking().Where(p => p.ProjectId == projectId && p.Type == PbiType.Task &&
                    p.ParentStoryId.HasValue && stories.Contains(p.ParentStoryId.Value)).Select(p => p.PbiId).ToListAsync(ct);
                await InvalidateChecksAsync(projectId, tasks, ct);
            }
        }
        else
        {
            if (request.ExpectedVersion.HasValue) throw new ArgumentException("New statements cannot have an expected version.");
            problem = new() { ProjectId = projectId, Title = request.Title.Trim(), Text = request.Text };
            db.ProblemStatements.Add(problem);
        }
        problem.Title = request.Title.Trim();
        problem.Text = request.Text;
        problem.IsArchived = request.IsArchived;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await NotifyAfterCommitAsync(projectId, ct);
        return problem;
    }

    public async Task<ProjectStoryBatchResult> GenerateAsync(int projectId, GenerateProjectStoriesRequest request,
        string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 160 ||
            request.ProblemStatementIds is null || request.ProblemStatementIds.Count is < 1 or > 20 ||
            request.ProblemStatementIds.Distinct().Count() != request.ProblemStatementIds.Count)
            throw new ArgumentException("Supply an idempotency key (1–160 characters) and 1–20 distinct statement IDs.");
        // Validate the entire selection before invoking AI or creating any run.
        var sources = await db.ProblemStatements.AsNoTracking().Where(p => p.ProjectId == projectId &&
            request.ProblemStatementIds.Contains(p.ProblemStatementId)).ToListAsync(ct);
        if (sources.Count != request.ProblemStatementIds.Count) throw new KeyNotFoundException();
        var results = new List<ProjectStoryRunResult>();
        foreach (var id in request.ProblemStatementIds)
        {
            ct.ThrowIfCancellationRequested();
            var source = sources.Single(p => p.ProblemStatementId == id);
            var key = $"stories:{id}:" + Hash(request.IdempotencyKey);
            var existing = await db.GenerationRuns.AsNoTracking().SingleOrDefaultAsync(r =>
                r.ProjectId == projectId && r.Kind == GenerationKind.Stories && r.IdempotencyKey == key, ct);
            if (existing is not null)
            {
                results.Add(await RunAsync(projectId, existing.GenerationRunId, ct));
                continue;
            }
            if (source.IsArchived)
            {
                results.Add(new(id, null, GenerationState.Failed, "source_archived", []));
                continue;
            }
            var guidelines = await new ProjectGuidelinesService(db).CurrentAsync(projectId, ct);
            var snapshot = new ProjectStoryInputSnapshot(source.ProblemStatementId, source.Version, source.Title,
                source.Text, guidelines?.ProjectGuidelinesRevisionId, guidelines?.Content);
            var run = new GenerationRun
            {
                ProjectId = projectId,
                Kind = GenerationKind.Stories,
                Provider = "local-ollama",
                Model = configuration?["OllamaModel"],
                IdempotencyKey = key,
                ActorId = actor,
                State = GenerationState.Running
            };
            // The unique run key is committed before the provider call. Concurrent submissions cannot generate twice.
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                db.GenerationRuns.Add(run);
                try
                {
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException ex)
                {
                    throw new InvalidOperationException("This generation request was submitted concurrently. Retry with the same key.", ex);
                }
            }
            ProjectStoryAiOutput? output = null;
            try
            {
                output = await ai.GenerateAsync<ProjectStoryAiOutput>(Instructions, snapshot, ct);
                if (output.Stories is null || output.Stories.Count is < 1 or > 100)
                    throw new JsonException("Story output must contain 1–100 stories.");
                foreach (var story in output.Stories)
                {
                    if (story is null) throw new JsonException("Story output contains an empty item.");
                    ValidateStory(story.Title, story.Description);
                    if (!Enum.IsDefined(story.Priority) || !Enum.IsDefined(story.StoryPoints))
                        throw new JsonException("Story output contains invalid planning values.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or ArgumentException or
                InvalidOperationException or OperationCanceledException)
            {
                run.State = ex is OperationCanceledException ? GenerationState.Cancelled : GenerationState.Failed;
                run.ErrorCode = ex switch
                {
                    OperationCanceledException => "cancelled",
                    HttpRequestException => "local_provider_unavailable",
                    InvalidOperationException => "local_provider_not_ready",
                    _ => "invalid_story_output"
                };
                run.CompletedAt = DateTime.UtcNow;
                db.GenerationInputs.Add(Input(run, snapshot, []));
                await db.SaveChangesAsync(CancellationToken.None);
                results.Add(new(id, run.GenerationRunId, run.State, run.ErrorCode, []));
                if (ct.IsCancellationRequested) break;
                continue;
            }
            var candidates = output.Stories.Select((story, index) => new StoredStoryProposal(index + 1, $"story-{index + 1}", story)).ToArray();
            db.GenerationInputs.Add(Input(run, snapshot, candidates));
            run.State = GenerationState.Succeeded;
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            results.Add(await RunAsync(projectId, run.GenerationRunId, ct));
        }
        return new(results);
    }

    public async Task<List<ProjectStoryRunResult>> RunsAsync(int projectId, int? sourceId, CancellationToken ct)
    {
        var prefix = $"stories:{sourceId}:";
        var ids = await db.GenerationRuns.AsNoTracking().Where(run => run.ProjectId == projectId &&
            run.Kind == GenerationKind.Stories && (!sourceId.HasValue || run.IdempotencyKey.StartsWith(prefix) ||
                db.GenerationInputs.Any(i => i.ProjectId == projectId && i.GenerationRunId == run.GenerationRunId && i.ProblemStatementId == sourceId)))
            .OrderByDescending(run => run.GenerationRunId).Select(run => run.GenerationRunId).ToListAsync(ct);
        var results = new List<ProjectStoryRunResult>();
        foreach (var id in ids) results.Add(await RunAsync(projectId, id, ct));
        return results;
    }

    public async Task<ProjectStoryRunResult> RunAsync(int projectId, int runId, CancellationToken ct)
    {
        var run = await db.GenerationRuns.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId &&
            r.GenerationRunId == runId && r.Kind == GenerationKind.Stories, ct) ?? throw new KeyNotFoundException();
        var input = await db.GenerationInputs.AsNoTracking().SingleOrDefaultAsync(i => i.ProjectId == projectId &&
            i.GenerationRunId == runId, ct);
        if (input is null)
        {
            var parts = run.IdempotencyKey.Split(':');
            var sourceId = parts.Length == 3 && int.TryParse(parts[1], out var id) ? id : 0;
            return new(sourceId, runId, run.State, run.ErrorCode, []);
        }
        var snapshot = JsonSerializer.Deserialize<StoredStorySnapshot>(input.SnapshotJson, SnapshotJsonOptions);
        if (snapshot?.Proposals is null)
            return new(input.ProblemStatementId ?? 0, runId, run.State, run.ErrorCode, []);
        var items = await db.Stories.AsNoTracking().Where(p => p.ProjectId == projectId &&
            p.GenerationRunId == runId).OrderBy(p => p.PbiId).ToListAsync(ct);
        var proposals = new List<StoryProposalDto>();
        var commonCurrency = await ProjectStoryCurrency.FromInputAsync(db, projectId, input.ProblemStatementId, input, ct);
        var itemIds = items.Select(p => p.PbiId).ToArray();
        var savedCurrencies = await ProjectStoryCurrency.ReadManyAsync(db, projectId, itemIds, ct);
        foreach (var candidate in snapshot.Proposals)
        {
            var accepted = items.SingleOrDefault(p => p.GenerationCandidateKey == candidate.CandidateKey);
            var item = accepted ?? NewStory(projectId, runId, input.ProblemStatementId!.Value, candidate, candidate.Content, false);
            proposals.Add(new(candidate.ProposalId, input.ProblemStatementId!.Value, runId, item,
                accepted is not null ? savedCurrencies[accepted.PbiId] : commonCurrency,
                accepted?.PbiId));
        }
        return new(input.ProblemStatementId!.Value, runId, run.State, run.ErrorCode, proposals);
    }

    public async Task<List<ProductBacklogItem>> AcceptAsync(int projectId, AcceptProjectStoriesRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count is < 1 or > 100 || request.Items.Any(i => i is null) ||
            request.Items.Select(i => i.ProposalId).Distinct().Count() != request.Items.Count)
            throw new ArgumentException("Accept 1–100 distinct proposals.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var run = await db.GenerationRuns.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId &&
            r.GenerationRunId == request.GenerationRunId && r.Kind == GenerationKind.Stories, ct)
            ?? throw new KeyNotFoundException();
        if (run.State != GenerationState.Succeeded) throw new InvalidOperationException("Only a successful run can be accepted.");
        var input = await db.GenerationInputs.AsNoTracking().SingleAsync(i => i.ProjectId == projectId &&
            i.GenerationRunId == run.GenerationRunId, ct);
        var snapshot = JsonSerializer.Deserialize<StoredStorySnapshot>(input.SnapshotJson, SnapshotJsonOptions);
        if (snapshot?.Input is null || snapshot.Proposals is null || snapshot.Input.ProblemStatementId != input.ProblemStatementId)
            throw new InvalidOperationException("The run has no valid immutable proposal snapshot.");
        var candidates = snapshot.Proposals.ToDictionary(p => p.ProposalId);
        if (request.Items.Any(i => !candidates.ContainsKey(i.ProposalId))) throw new KeyNotFoundException();
        var keys = request.Items.Select(i => candidates[i.ProposalId].CandidateKey).ToArray();
        var existing = await db.Stories.Where(p => p.GenerationRunId == run.GenerationRunId &&
            keys.Contains(p.GenerationCandidateKey!)).ToListAsync(ct);
        foreach (var edit in request.Items)
        {
            ValidateStory(edit.Title, edit.Description);
            if (!Enum.IsDefined(edit.Priority) || !Enum.IsDefined(edit.StoryPoints)) throw new ArgumentException("Invalid story values.");
            var item = existing.SingleOrDefault(p => p.GenerationCandidateKey == candidates[edit.ProposalId].CandidateKey);
            if (item is null)
            {
                if (edit.ExpectedVersion != 0) throw new DbUpdateConcurrencyException();
                continue;
            }
            if (item.ProjectId != projectId || item.ProblemStatementId != input.ProblemStatementId ||
                item.Type != PbiType.Story || item.ParentStoryId.HasValue) throw new InvalidOperationException("Proposal lineage does not match.");
            if (!Matches(item, edit, request.AsDraft))
                throw new InvalidOperationException("This candidate was already accepted. Edit the saved item instead of accepting it again.");
        }
        var items = new List<ProductBacklogItem>();
        var hasNew = false;
        foreach (var edit in request.Items)
        {
            var candidate = candidates[edit.ProposalId];
            var item = existing.SingleOrDefault(p => p.GenerationCandidateKey == candidate.CandidateKey);
            if (item is null)
            {
                item = NewStory(projectId, run.GenerationRunId, input.ProblemStatementId!.Value, candidate,
                    new(edit.Title.Trim(), edit.Description, edit.Priority, edit.StoryPoints), request.AsDraft);
                db.Stories.Add(item);
                hasNew = true;
            }
            items.Add(item);
        }
        try
        {
            if (hasNew) await SaveStoriesAsync(ct);
            var revision = await db.ProjectGraphStates.Where(s => s.ProjectId == projectId).Select(s => s.Revision).SingleOrDefaultAsync(ct);
            await tx.CommitAsync(ct);
            if (hasNew && notifier is not null) await notifier.NotifyAsync(projectId, revision, ct);
        }
        catch (DbUpdateException ex)
        {
            throw new DbUpdateConcurrencyException("Proposal acceptance changed concurrently. Reload or retry the same selection.", ex);
        }
        return items;
    }

    public async Task<ProductBacklogItem> LinkLegacyAsync(int projectId, int pbiId, LinkLegacyStoryRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var pbi = await db.Stories.SingleOrDefaultAsync(p => p.ProjectId == projectId && p.PbiId == pbiId, ct)
            ?? throw new KeyNotFoundException();
        if (pbi.Version != request.ExpectedVersion) throw new DbUpdateConcurrencyException();
        if (pbi.ProblemStatementId.HasValue || pbi.GenerationRunId.HasValue || pbi.Type != PbiType.Story)
            throw new InvalidOperationException("Only an unlinked legacy story can be manually linked.");
        if (!await db.ProblemStatements.AnyAsync(p => p.ProjectId == projectId &&
            p.ProblemStatementId == request.ProblemStatementId && !p.IsArchived, ct)) throw new KeyNotFoundException();
        var children = await db.Stories.AsNoTracking().Where(p => p.ProjectId == projectId &&
            p.Type == PbiType.Task && p.ParentStoryId == pbiId).Select(p => p.PbiId).ToListAsync(ct);
        await InvalidateChecksAsync(projectId, children, ct);
        pbi.ProblemStatementId = request.ProblemStatementId;
        pbi.LastUpdated = DateTime.UtcNow;
        await SaveStoriesAsync(ct);
        await tx.CommitAsync(ct);
        await NotifyAfterCommitAsync(projectId, ct);
        return pbi;
    }

    public async Task<PlanningArtifactCurrency> ReviewApplicabilityAsync(int projectId, int storyId,
        ReviewStoryApplicabilityRequest request, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExpectedFingerprint) || request.ExpectedFingerprint.Length != 64 ||
            !request.ExpectedFingerprint.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 2000)
            throw new ArgumentException("Supply the current fingerprint and a review reason of 1–2000 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        if (!await db.Stories.AsNoTracking().AnyAsync(p => p.ProjectId == projectId && p.PbiId == storyId && p.Type == PbiType.Story, ct))
            throw new KeyNotFoundException();
        var currency = await ProjectStoryCurrency.ReadAsync(db, projectId, storyId, ct);
        if (currency.CurrentFingerprint != request.ExpectedFingerprint) throw new DbUpdateConcurrencyException("Story inputs changed; reload before reviewing.");
        var children = await db.Stories.AsNoTracking().Where(p => p.ProjectId == projectId &&
            p.Type == PbiType.Task && p.ParentStoryId == storyId).Select(p => p.PbiId).ToListAsync(ct);
        await InvalidateChecksAsync(projectId, children, ct);
        var review = new PlanningArtifactReview
        {
            ProjectId = projectId,
            PbiId = storyId,
            Fingerprint = currency.CurrentFingerprint,
            ActorId = actor,
            CreatedAt = DateTime.UtcNow,
            Reason = request.Reason.Trim()
        };
        db.PlanningArtifactReviews.Add(review);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await NotifyAfterCommitAsync(projectId, ct);
        return currency with
        {
            ApplicabilityReviewed = true,
            ApplicabilityReview = review,
            ContentChanged = false,
            SourceChanged = false,
            GuidelinesChanged = false,
            SourceArchived = false
        };
    }

    public Task<PlanningArtifactCurrency> CurrencyAsync(int projectId, int pbiId, CancellationToken ct) =>
        ProjectStoryCurrency.ReadAsync(db, projectId, pbiId, ct);

    private static GenerationInput Input(GenerationRun run, ProjectStoryInputSnapshot source, IReadOnlyList<StoredStoryProposal> proposals) => new()
    {
        ProjectId = run.ProjectId,
        GenerationRunId = run.GenerationRunId,
        ProblemStatementId = source.ProblemStatementId,
        ProjectGuidelinesRevisionId = source.GuidelinesRevisionId,
        SnapshotJson = JsonSerializer.Serialize(new StoredStorySnapshot(source, proposals), SnapshotJsonOptions)
    };

    private static ProductBacklogItem NewStory(int projectId, int runId, int sourceId, StoredStoryProposal candidate,
        ProjectStoryAiContent content, bool draft) => new()
        {
            ProjectId = projectId,
            ProblemStatementId = sourceId,
            GenerationRunId = runId,
            GenerationCandidateKey = candidate.CandidateKey,
            Title = content.Title.Trim(),
            Description = content.Description,
            Priority = content.Priority,
            StoryPoints = content.StoryPoints,
            Type = PbiType.Story,
            Origin = PbiOrigin.AiGenerated,
            IsDraft = draft,
            Status = PbiStatus.ToDo,
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

    private Task SaveStoriesAsync(CancellationToken ct) => planning.SaveAsync(ct);
    private async Task NotifyAfterCommitAsync(int projectId, CancellationToken ct)
    {
        if (notifier is null) return;
        var revision = await db.ProjectGraphStates.AsNoTracking().Where(s => s.ProjectId == projectId)
            .Select(s => s.Revision).SingleOrDefaultAsync(ct);
        await notifier.NotifyAsync(projectId, revision, ct);
    }
    private async Task InvalidateChecksAsync(int projectId, IReadOnlyList<int> tasks, CancellationToken ct)
    {
        foreach (var taskId in tasks)
            await (scopes ?? throw new InvalidOperationException("Task check invalidation is unavailable; source inputs were not changed."))
                .InvalidateTaskAsync(projectId, taskId, ct);
    }
    private static bool Matches(ProductBacklogItem p, AcceptStoryProposal e, bool draft) =>
        p.Title == e.Title.Trim() && p.Description == e.Description && p.Priority == e.Priority &&
        p.StoryPoints == e.StoryPoints && p.IsDraft == draft;
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void ValidateText(string title, string text)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > 128 * 1024)
            throw new ArgumentException("A title (1–200 characters) and nonempty text (up to 128 KiB UTF-8) are required.");
    }
    private static void ValidateStory(string title, string description)
    {
        ValidateText(title, description);
        if (description.Length > 2000) throw new ArgumentException("Story descriptions, including acceptance criteria, must not exceed 2000 characters.");
    }
}
