using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class TaskGenerationService(OrganizationContext db, LocalPlanningAi ai,
    RepositoryContextService repositories, ProjectDependencyService dependencies, TaskSpecificationService specs,
    ProjectWorkNotifier notifier, ScrumPilot.API.Configuration.AiOptions options, ILogger<TaskGenerationService> logger,
    PlanningInferenceSession inference, GitHubRepositoryRegistry registry)
{
    public async Task<TaskGenerationPreviewDto> PreviewAsync(int projectId, GenerateTaskPreviewRequest request, string actor, CancellationToken ct)
    {
        if (request.StoryIds is null || request.SourcePaths is null || request.StoryIds.Length is 0 or > 10 || request.StoryIds.Distinct().Count() != request.StoryIds.Length
            || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            throw new ArgumentException("Select 1-10 distinct stories and provide an idempotency key of at most 100 characters.");
        var existing = await db.GenerationRuns.SingleOrDefaultAsync(run => run.ProjectId == projectId
            && run.Kind == GenerationKind.Tasks && run.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            var input = await db.GenerationInputs.SingleOrDefaultAsync(input => input.GenerationRunId == existing.GenerationRunId, ct);
            if (existing.State == GenerationState.Succeeded && input is not null)
            {
                using var saved = JsonDocument.Parse(input.SnapshotJson);
                if (!saved.RootElement.TryGetProperty("Request", out var previous)
                    || Fingerprint(previous.Deserialize<GenerateTaskPreviewRequest>()!) != Fingerprint(request))
                    throw new InvalidOperationException("This idempotency key belongs to different generation inputs.");
                return JsonSerializer.Deserialize<TaskGenerationPreviewDto>(input.SnapshotJson)!;
            }
            throw new InvalidOperationException("This generation request is running or failed. Use a new key for an explicit regeneration.");
        }
        var stories = await db.Stories.Where(item => item.ProjectId == projectId && request.StoryIds.Contains(item.PbiId)
            && item.Type == PbiType.Story && !item.IsDraft).ToListAsync(ct);
        if (stories.Count != request.StoryIds.Length) throw new ArgumentException("Every selected source must be a committed story in this project.");
        if (stories.Any(story => story.Status == PbiStatus.Done)) throw new InvalidOperationException("Reopen a completed story before generating new child work.");
        var snapshot = await db.RepositorySnapshots.SingleOrDefaultAsync(item => item.ProjectId == projectId
            && item.RepositorySnapshotId == request.RepositorySnapshotId, ct) ?? throw new KeyNotFoundException();
        var project = await db.Projects.SingleAsync(item => item.ProjectId == projectId, ct);
        var guidance = await db.ProjectGuidelinesRevisions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.ProjectGuidelinesRevisionId == project.CurrentGuidelinesRevisionId, ct);
        if (request.SourcePaths is null || request.SourcePaths.Length == 0) throw new ArgumentException("Select relevant verified source files before generating an implementation specification.");
        var source = await repositories.SourceAsync(projectId, snapshot.RepositorySnapshotId, request.SourcePaths, ct);
        var run = new GenerationRun
        {
            ProjectId = projectId,
            Kind = GenerationKind.Tasks,
            Provider = "Ollama",
            Model = options.Model,
            IdempotencyKey = request.IdempotencyKey,
            ActorId = actor,
            State = GenerationState.Running
        };
        db.GenerationRuns.Add(run);
        await db.SaveChangesAsync(ct);
        const string instructions = """
                Return only a JSON object with a "tasks" array. Decompose each selected story into small actionable implementation tasks.
                Every task must have exactly: key (unique short string), parentStoryId (one of the input story IDs), title,
                content, files, dependsOnKeys (other proposed task keys), existingPrerequisites (existing task PBI IDs or []).
                content must have: schemaVersion:1, goal, requirements:string[], acceptanceCriteria:string[],
                verificationSteps:string[] (exact commands and expected results), outOfScope:string[], currentContext:string,
                implementationSteps:string[], documentation:string[], definitionOfDone:string[],
                sourceStoryVersion:0, sourceStorySnapshot:"".
                files must contain exact paths and use category Source/Test/Documentation/Configuration,
                operation Add/Modify/Delete/Rename, rationale, and renameFromPath:null unless renaming.
                Include associated test files and appropriate documentation files (or explicit no-doc rationale in content).
                Existing paths must appear in the supplied tree. New files require named paths and justification.
                Do not broaden scope to whole directories, add unrelated changes, or invent acceptance criteria.
                Apply project guidelines subject to application security and this output contract. Propose real dependencies,
                not automatic dependencies merely because tasks share a source. Do not execute code.
                """;
        var context = new
        {
            PromptVersion = 1,
            GraphRevision = await db.ProjectGraphStates.AsNoTracking().Where(item => item.ProjectId == projectId)
                .Select(item => item.Revision).SingleOrDefaultAsync(ct),
            SourceCurrency = (await ProjectStoryCurrency.ReadManyAsync(db, projectId, request.StoryIds, ct))
                .ToDictionary(item => item.Key, item => item.Value.CurrentFingerprint),
            Stories = stories.Select(story => new { story.PbiId, story.Title, story.Description }),
            Guidelines = guidance?.Content,
            snapshot.CommitSha,
            Tree = RepositoryContextService.ReadTree(snapshot).Entries,
            Source = source,
            ExistingTasks = await db.Stories.AsNoTracking().Where(item => item.ProjectId == projectId && item.Type == PbiType.Task)
                        .Select(item => new { item.PbiId, item.Title, item.ParentStoryId, item.Status }).ToArrayAsync(ct),
            Dependencies = await db.PbiDependencies.AsNoTracking().Where(item => item.ProjectId == projectId)
                        .Select(item => new { item.DependentPbiId, item.PrerequisitePbiId }).ToArrayAsync(ct)
        };
        try
        {
            var output = await inference.RunAsync(projectId,
                () => ai.GenerateAsync<TaskGenerationOutput>(instructions, context, ct), ct);
            db.Attach(run);
            await using var resultTransaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await db.AcquirePlanningPolicyLockAsync(projectId, ct);
            var currentProject = await db.Projects.AsNoTracking().SingleAsync(item => item.ProjectId == projectId, ct);
            var currentStories = await db.Stories.AsNoTracking().Where(item => item.ProjectId == projectId
                && request.StoryIds.Contains(item.PbiId)).ToListAsync(ct);
            var repository = await db.ProjectRepositories.AsNoTracking().SingleAsync(item =>
                item.ProjectId == projectId && item.ProjectRepositoryId == snapshot.ProjectRepositoryId, ct);
            await registry.RequireCurrentAsync(repository, ct);
            var currentCurrency = await ProjectStoryCurrency.ReadManyAsync(db, projectId, request.StoryIds, ct);
            var currentGraphRevision = await db.ProjectGraphStates.AsNoTracking().Where(item => item.ProjectId == projectId)
                .Select(item => item.Revision).SingleOrDefaultAsync(ct);
            if (currentProject.CurrentGuidelinesRevisionId != guidance?.ProjectGuidelinesRevisionId
                || currentGraphRevision != context.GraphRevision
                || context.SourceCurrency.Any(item => !currentCurrency.TryGetValue(item.Key, out var currency)
                    || currency.CurrentFingerprint != item.Value)
                || currentStories.Count != stories.Count || currentStories.Any(current =>
                    current.Version != stories.Single(story => story.PbiId == current.PbiId).Version
                    || current.IsDraft || current.Type != PbiType.Story || current.Status == PbiStatus.Done)
                || !await db.RepositorySnapshots.AsNoTracking().AnyAsync(item => item.ProjectId == projectId
                    && item.RepositorySnapshotId == snapshot.RepositorySnapshotId && item.CommitSha == snapshot.CommitSha, ct))
                throw new InvalidOperationException("The selected sources or guidance changed during inference; regenerate.");
            if (output.Tasks is null || output.Tasks.Length is 0 or > 50 || output.Tasks.Any(task => task is null)
                || output.Tasks.Select(task => task.Key).Distinct().Count() != output.Tasks.Length)
                throw new ArgumentException("The generated task set must contain 1-50 tasks with unique keys.");
            var keys = output.Tasks.Select(task => task.Key).ToHashSet(StringComparer.Ordinal);
            var tasks = output.Tasks.Select(task =>
            {
                var story = stories.SingleOrDefault(story => story.PbiId == task.ParentStoryId)
                    ?? throw new ArgumentException("AI proposed an unknown parent story.");
                if (string.IsNullOrWhiteSpace(task.Key) || task.Key.Length > 100 || string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 200
                    || task.DependsOnKeys is null || task.ExistingPrerequisites is null
                    || task.DependsOnKeys.Distinct().Count() != task.DependsOnKeys.Length
                    || task.DependsOnKeys.Any(key => !keys.Contains(key) || key == task.Key)
                    || task.ExistingPrerequisites.Any(id => !context.ExistingTasks.Any(existing => existing.PbiId == id)))
                    throw new ArgumentException("AI proposed invalid task identities or prerequisites.");
                TaskSpecificationService.ValidateContent(task.Content);
                TaskSpecificationService.VerifyFiles(task.Files, snapshot);
                return task with
                {
                    Content = task.Content with
                    {
                        SourceStoryVersion = story.Version,
                        SourceStorySnapshot = TaskSpecificationService.SourceText(story)
                    }
                };
            }).ToArray();
            var preview = new TaskGenerationPreviewDto(run.GenerationRunId, snapshot.RepositorySnapshotId, guidance?.ProjectGuidelinesRevisionId, tasks);
            db.GenerationInputs.Add(new()
            {
                ProjectId = projectId,
                GenerationRunId = run.GenerationRunId,
                ProjectGuidelinesRevisionId = guidance?.ProjectGuidelinesRevisionId,
                RepositorySnapshotId = snapshot.RepositorySnapshotId,
                SnapshotJson = JsonSerializer.Serialize(new
                {
                    preview.GenerationRunId,
                    preview.RepositorySnapshotId,
                    preview.GuidelinesRevisionId,
                    preview.Tasks,
                    Request = request,
                    Prompt = instructions,
                    Context = context
                })
            });
            run.State = GenerationState.Succeeded;
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await resultTransaction.CommitAsync(ct);
            return preview;
        }
        catch (Exception ex) when (db.HasStorageLease && ex is (HttpRequestException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException))
        {
            if (db.Entry(run).State == EntityState.Detached) db.Attach(run);
            run.State = ex is OperationCanceledException ? GenerationState.Cancelled : GenerationState.Failed;
            run.ErrorCode = ex.GetType().Name;
            run.CompletedAt = DateTime.UtcNow;
            if (!db.ChangeTracker.Entries<GenerationInput>().Any(entry => entry.Entity.GenerationRunId == run.GenerationRunId))
                db.GenerationInputs.Add(new()
                {
                    ProjectId = projectId,
                    GenerationRunId = run.GenerationRunId,
                    ProjectGuidelinesRevisionId = guidance?.ProjectGuidelinesRevisionId,
                    RepositorySnapshotId = snapshot.RepositorySnapshotId,
                    SnapshotJson = JsonSerializer.Serialize(new { Request = request, Prompt = instructions, Context = context, run.ErrorCode })
                });
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("Task generation run {RunId} failed with {ErrorCode}", run.GenerationRunId, run.ErrorCode);
            throw;
        }
    }

    public async Task<ProductBacklogItem[]> AcceptAsync(int projectId, AcceptTaskProposalsRequest request, string actor, CancellationToken ct)
    {
        if (request.Tasks is null || request.Tasks.Length is 0 or > 50 || request.Tasks.Any(task => task is null)
            || request.Tasks.Select(task => task.Key).Distinct().Count() != request.Tasks.Length)
            throw new ArgumentException("Select distinct task proposals to accept.");
        if (!await db.GenerationRuns.AnyAsync(run => run.ProjectId == projectId && run.GenerationRunId == request.GenerationRunId
            && run.Kind == GenerationKind.Tasks && run.State == GenerationState.Succeeded, ct))
            throw new ArgumentException("Select a successful task-generation run in this project.");
        var input = await db.GenerationInputs.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.GenerationRunId == request.GenerationRunId, ct) ?? throw new KeyNotFoundException();
        var preview = JsonSerializer.Deserialize<TaskGenerationPreviewDto>(input.SnapshotJson)
            ?? throw new InvalidOperationException("The saved task preview is invalid.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var project = await db.Projects.SingleAsync(item => item.ProjectId == projectId, ct);
        if (preview.GuidelinesRevisionId != project.CurrentGuidelinesRevisionId)
            throw new InvalidOperationException("Guidelines changed. Regenerate or explicitly review a new task proposal.");
        var originals = preview.Tasks.ToDictionary(task => task.Key, StringComparer.Ordinal);
        using var savedInput = JsonDocument.Parse(input.SnapshotJson);
        var savedCurrency = savedInput.RootElement.TryGetProperty("Context", out var savedContext)
            && savedContext.TryGetProperty("SourceCurrency", out var currencyJson)
            ? currencyJson.Deserialize<Dictionary<int, string>>() : null;
        var accepted = await db.Stories.Where(item => item.ProjectId == projectId && item.GenerationRunId == request.GenerationRunId).ToListAsync(ct);
        var map = accepted.Where(item => item.GenerationCandidateKey is not null).ToDictionary(item => item.GenerationCandidateKey!, StringComparer.Ordinal);
        var added = new List<(TaskProposalDto Proposal, ProductBacklogItem Item)>();
        foreach (var task in request.Tasks)
        {
            if (!originals.TryGetValue(task.Key, out var original) || original.ParentStoryId != task.ParentStoryId)
                throw new ArgumentException("A task candidate does not match the saved generation preview.");
            if (map.ContainsKey(task.Key)) continue;
            var parent = await db.Stories.SingleOrDefaultAsync(item => item.ProjectId == projectId
                && item.PbiId == task.ParentStoryId && item.Type == PbiType.Story && !item.IsDraft, ct) ?? throw new KeyNotFoundException();
            var currency = await ProjectStoryCurrency.ReadAsync(db, projectId, parent.PbiId, ct);
            if (TaskSpecificationService.SourceText(parent) != original.Content.SourceStorySnapshot
                || parent.Version != original.Content.SourceStoryVersion
                || savedCurrency is null || !savedCurrency.TryGetValue(parent.PbiId, out var fingerprint)
                || currency.CurrentFingerprint != fingerprint)
                throw new InvalidOperationException("The source story changed. Review/regenerate task proposals before acceptance.");
            if (string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 200) throw new ArgumentException("Task titles must be 1-200 characters.");
            TaskSpecificationService.ValidateContent(task.Content);
            if (task.DependsOnKeys is null || task.ExistingPrerequisites is null
                || task.DependsOnKeys.Distinct().Count() != task.DependsOnKeys.Length)
                throw new ArgumentException("Use distinct task dependency keys.");
            var item = new ProductBacklogItem
            {
                ProjectId = projectId,
                ParentStoryId = parent.PbiId,
                SprintId = parent.SprintId,
                Title = task.Title,
                Description = task.Content.Goal,
                GenerationRunId = request.GenerationRunId,
                GenerationCandidateKey = task.Key,
                Type = PbiType.Task,
                Origin = PbiOrigin.AiGenerated,
                IsDraft = request.AsDraft,
                Status = PbiStatus.ToDo
            };
            db.Stories.Add(item);
            map[task.Key] = item;
            added.Add((task, item));
        }
        await dependencies.SaveAsync(ct);
        foreach (var (proposal, item) in added)
        {
            await specs.SaveAsync(projectId, item.PbiId, new(proposal.Content, proposal.Files, preview.RepositorySnapshotId, 0), actor, ct);
            foreach (var key in proposal.DependsOnKeys)
            {
                if (!map.TryGetValue(key, out var prerequisite)) throw new ArgumentException("Accept required task proposals before their dependents.");
                db.PbiDependencies.Add(new() { ProjectId = projectId, DependentPbiId = item.PbiId, PrerequisitePbiId = prerequisite.PbiId, ActorId = actor });
            }
            foreach (var id in proposal.ExistingPrerequisites.Distinct())
            {
                if (!await db.Stories.AnyAsync(existing => existing.ProjectId == projectId && existing.PbiId == id && existing.Type == PbiType.Task, ct))
                    throw new ArgumentException("Existing prerequisites must be tasks in this project.");
                db.PbiDependencies.Add(new() { ProjectId = projectId, DependentPbiId = item.PbiId, PrerequisitePbiId = id, ActorId = actor });
            }
        }
        await db.SaveChangesAsync(ct);
        var items = await db.Stories.Where(item => item.ProjectId == projectId).ToListAsync(ct);
        var edges = await db.PbiDependencies.Where(edge => edge.ProjectId == projectId).ToListAsync(ct);
        if (DependencyGraphEvaluator.Evaluate(items, edges.Select(edge => new DependencyLink(edge.DependentPbiId, edge.PrerequisitePbiId))).HasCycle)
            throw new ArgumentException("The accepted task dependencies create an effective execution cycle.");
        await transaction.CommitAsync(ct);
        var revision = await db.ProjectGraphStates.Where(state => state.ProjectId == projectId).Select(state => state.Revision).SingleAsync(ct);
        await notifier.NotifyAsync(projectId, revision, ct);
        return request.Tasks.Select(task => map[task.Key]).ToArray();
    }

    private static string Fingerprint(GenerateTaskPreviewRequest request) =>
        JsonSerializer.Serialize(new
        {
            request.RepositorySnapshotId,
            Stories = request.StoryIds.Order(),
            Paths = request.SourcePaths.Order(StringComparer.Ordinal)
        });
}
