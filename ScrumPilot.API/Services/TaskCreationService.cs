using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Configuration;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class TaskCreationService(
    OrganizationContext db,
    LocalPlanningAi ai,
    ProjectDependencyService dependencies,
    ProjectWorkNotifier notifier,
    AiOptions options,
    ILogger<TaskCreationService> logger,
    PlanningInferenceSession inference)
{
    public async Task<TaskCreationPreviewDto> PreviewAsync(int projectId,
        GenerateTaskCreationPreviewRequest request, string actor, CancellationToken ct)
    {
        if (request.ParentPbiIds is null || request.ParentPbiIds.Length is 0 or > 10
            || request.ParentPbiIds.Distinct().Count() != request.ParentPbiIds.Length
            || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 100)
            throw new ArgumentException("Select 1-10 distinct source work items and provide an idempotency key of at most 100 characters.");

        var existingRun = await db.GenerationRuns.SingleOrDefaultAsync(run => run.ProjectId == projectId
            && run.Kind == GenerationKind.Tasks && run.IdempotencyKey == request.IdempotencyKey, ct);
        if (existingRun is not null)
        {
            var savedInput = await db.GenerationInputs.SingleOrDefaultAsync(input =>
                input.GenerationRunId == existingRun.GenerationRunId, ct);
            if (existingRun.State == GenerationState.Succeeded && savedInput is not null)
            {
                using var saved = JsonDocument.Parse(savedInput.SnapshotJson);
                var previous = saved.RootElement.GetProperty("Request").Deserialize<GenerateTaskCreationPreviewRequest>()
                    ?? throw new InvalidOperationException("The saved task request is invalid.");
                if (Fingerprint(previous) != Fingerprint(request))
                    throw new InvalidOperationException("This idempotency key belongs to different task-generation inputs.");
                return saved.RootElement.GetProperty("Preview").Deserialize<TaskCreationPreviewDto>()
                    ?? throw new InvalidOperationException("The saved task preview is invalid.");
            }
            throw new InvalidOperationException("This task-generation request is running or failed. Use a new key to retry.");
        }

        var sources = await db.Stories.Where(item => item.ProjectId == projectId
            && request.ParentPbiIds.Contains(item.PbiId)
            && (item.Type == PbiType.Story || item.Type == PbiType.Bug) && !item.IsDraft).ToListAsync(ct);
        if (sources.Count != request.ParentPbiIds.Length)
            throw new ArgumentException("Every selected source must be a committed story or bug in this project.");
        if (sources.Any(source => source.Status == PbiStatus.Done))
            throw new InvalidOperationException("Reopen completed source work before generating child tasks.");

        var existingGenerated = await db.Stories.AsNoTracking().Where(item => item.ProjectId == projectId
            && item.Type == PbiType.Task && item.Origin == PbiOrigin.AiGenerated
            && item.ParentStoryId.HasValue && request.ParentPbiIds.Contains(item.ParentStoryId.Value))
            .Select(item => new { item.PbiId, item.ParentStoryId, item.Title, item.Status }).ToArrayAsync(ct);
        if (!request.ReplaceGeneratedTasks && existingGenerated.Length > 0)
            throw new InvalidOperationException("Generated tasks already exist for a selected source. Use explicit regeneration to replace them.");
        if (request.ReplaceGeneratedTasks && existingGenerated.Length == 0)
            throw new InvalidOperationException("There are no generated tasks to replace for the selected source.");

        var project = await db.Projects.SingleAsync(item => item.ProjectId == projectId, ct);
        // var guidance = await db.ProjectGuidelinesRevisions.AsNoTracking().SingleOrDefaultAsync(item =>
        //     item.ProjectId == projectId && item.ProjectGuidelinesRevisionId == project.CurrentGuidelinesRevisionId, ct);
        var graphRevision = await db.ProjectGraphStates.AsNoTracking().Where(item => item.ProjectId == projectId)
            .Select(item => item.Revision).SingleOrDefaultAsync(ct);
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

        var prompt = $$"""
            Return only a JSON object with a "tasks" array. Decompose each selected Story or Bug into small, independently actionable ScrumPilot tasks.
            Adhere to the supplied current project guidelines. Use only the selected source work items and project context; do not assume repository or file access.
            Every task must have exactly: key (unique short string), parentPbiId (one of the input PBI IDs), title, description,
            dependsOnKeys (other proposed task keys), and existingPrerequisites (existing task PBI IDs or []).
            Give each task a clear outcome and enough detail to implement it without inventing requirements. Propose only real dependencies.
            {{(request.ReplaceGeneratedTasks
                ? "This is a replacement run. ExistingPrerequisites must not reference any generated task that is being replaced; use only persisted manual tasks."
                : "")}}
            The final copy/pastable implementation spec is assembled by the server and always includes the required project-guideline and agent-handoff instructions.
            Do not output identifiers other than parentPbiId and task dependency keys. Do not execute code.
            """;
        var existingTasks = await db.Stories.AsNoTracking().Where(item =>
            item.ProjectId == projectId && item.Type == PbiType.Task
            && item.ParentStoryId.HasValue && request.ParentPbiIds.Contains(item.ParentStoryId.Value))
            .Select(item => new { item.PbiId, item.Title, item.ParentStoryId, item.Status }).ToArrayAsync(ct);
        var replacedGeneratedIds = request.ReplaceGeneratedTasks
            ? existingGenerated.Select(task => task.PbiId).ToHashSet()
            : new HashSet<int>();
        var existingTaskIds = existingTasks
            .Select(task => task.PbiId)
            .Where(id => !replacedGeneratedIds.Contains(id))
            .ToHashSet();
        var context = new
        {
            PromptVersion = 1,
            GraphRevision = graphRevision,
            Sources = sources.Select(source => new
            {
                source.PbiId,
                source.Type,
                source.Title,
                source.Description,
                source.Version,
                SourceSnapshot = SourceText(source)
            }).ToArray(),
            // GuidelinesRevisionId = guidance?.ProjectGuidelinesRevisionId,
            // Guidelines = guidance?.Content,
            ExistingTasks = existingTasks,
            Dependencies = await db.PbiDependencies.AsNoTracking().Where(item => item.ProjectId == projectId
                    && existingTaskIds.Contains(item.DependentPbiId) && existingTaskIds.Contains(item.PrerequisitePbiId))
                .Select(item => new { item.DependentPbiId, item.PrerequisitePbiId }).ToArrayAsync(ct)
        };

        try
        {
            var output = await inference.RunAsync(projectId,
                () => ai.GenerateAsync<TaskCreationAiOutput>(prompt, context, ct), ct);
            db.Attach(run);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await db.AcquirePlanningPolicyLockAsync(projectId, ct);
            var currentProject = await db.Projects.AsNoTracking().SingleAsync(item => item.ProjectId == projectId, ct);
            var currentSources = await db.Stories.AsNoTracking().Where(item =>
                item.ProjectId == projectId && request.ParentPbiIds.Contains(item.PbiId)).ToListAsync(ct);
            var currentGeneratedCount = await db.Stories.AsNoTracking().CountAsync(item => item.ProjectId == projectId
                && item.Type == PbiType.Task && item.Origin == PbiOrigin.AiGenerated
                && item.ParentStoryId.HasValue && request.ParentPbiIds.Contains(item.ParentStoryId.Value), ct);
            var currentGraphRevision = await db.ProjectGraphStates.AsNoTracking().Where(item => item.ProjectId == projectId)
                .Select(item => item.Revision).SingleOrDefaultAsync(ct);
            if (//currentProject.CurrentGuidelinesRevisionId != guidance?.ProjectGuidelinesRevisionId
                // || 
                currentGraphRevision != graphRevision || currentSources.Count != sources.Count
                || currentSources.Any(current => current.Version != sources.Single(source => source.PbiId == current.PbiId).Version
                    || current.Type is not (PbiType.Story or PbiType.Bug) || current.IsDraft || current.Status == PbiStatus.Done)
                || request.ReplaceGeneratedTasks != (currentGeneratedCount > 0))
                throw new InvalidOperationException("The selected sources, tasks, or project guidelines changed during generation; reload and try again.");

            if (output.Tasks is null || output.Tasks.Length is 0 or > 50 || output.Tasks.Any(task => task is null)
                || output.Tasks.Select(task => task.Key).Distinct(StringComparer.Ordinal).Count() != output.Tasks.Length)
                throw new ArgumentException("Generated tasks must contain 1-50 proposals with unique keys.");
            var keys = output.Tasks.Select(task => task.Key).ToHashSet(StringComparer.Ordinal);
            var proposals = output.Tasks.Select(task =>
            {
                var source = sources.SingleOrDefault(item => item.PbiId == task.ParentPbiId)
                    ?? throw new ArgumentException("AI proposed an unknown parent work item.");
                if (string.IsNullOrWhiteSpace(task.Key) )
                    throw new ArgumentException("AI proposed a task with an empty key.");
                if (task.Key.Length > 100)
                    throw new ArgumentException("AI proposed a task with a key longer than 100 characters.");

                if( string.IsNullOrWhiteSpace(task.Title) )
                    throw new ArgumentException("AI proposed a task with an empty title.");
                
                if( task.Title.Length > 200)
                    throw new ArgumentException("AI proposed a task with a title longer than 200 characters.");

                if(string.IsNullOrWhiteSpace(task.Description))
                    throw new ArgumentException("AI proposed a task with an empty description.");
                
                if( task.Description.Length > 10000)
                    throw new ArgumentException("AI proposed a task with a description longer than 10000 characters.");

                if(task.DependsOnKeys is null)
                    throw new ArgumentException("AI proposed a task with null DependsOnKeys.");
                
                if( task.ExistingPrerequisites is null)
                    throw new ArgumentException("AI proposed a task with null ExistingPrerequisites.");

                if(task.DependsOnKeys.Distinct(StringComparer.Ordinal).Count() != task.DependsOnKeys.Length)
                    throw new ArgumentException("AI proposed a task with duplicate DependsOnKeys.");

                if(task.DependsOnKeys.Any(key => !keys.Contains(key) || key == task.Key))
                    throw new ArgumentException("AI proposed a task with invalid DependsOnKeys.");

                if(task.ExistingPrerequisites.Any(id => !existingTaskIds.Contains(id)))
                    throw new ArgumentException("AI proposed a task with invalid ExistingPrerequisites.");

                // if (request.ReplaceGeneratedTasks && task.ExistingPrerequisites.Any(id =>
                //         existingGenerated.Any(existing => existing.PbiId == id)))
                //     throw new ArgumentException("AI proposed a task with ExistingPrerequisites that are already generated.");
                
                return new TaskCreationProposalDto(task.Key, source.PbiId, task.Title, task.Description,
                    task.DependsOnKeys, task.ExistingPrerequisites, source.Version, SourceText(source));
            }).ToArray();

            var preview = new TaskCreationPreviewDto(run.GenerationRunId, /*guidance?.ProjectGuidelinesRevisionId,*/ proposals);
            db.GenerationInputs.Add(new()
            {
                ProjectId = projectId,
                GenerationRunId = run.GenerationRunId,
                // ProjectGuidelinesRevisionId = guidance?.ProjectGuidelinesRevisionId,
                SnapshotJson = JsonSerializer.Serialize(new { Request = request, Prompt = prompt, Context = context, Preview = preview })
            });
            run.State = GenerationState.Succeeded;
            run.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return preview;
        }
        catch (Exception ex) when (db.HasStorageLease && ex is
            (HttpRequestException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException))
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
                    // ProjectGuidelinesRevisionId = guidance?.ProjectGuidelinesRevisionId,
                    SnapshotJson = JsonSerializer.Serialize(new { Request = request, Prompt = prompt, Context = context, run.ErrorCode })
                });
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning("Task creation run {RunId} failed with {ErrorCode}", run.GenerationRunId, run.ErrorCode);
            throw;
        }
    }

    public async Task<ProductBacklogItem[]> AcceptAsync(int projectId,
        AcceptTaskCreationProposalsRequest request, string actor, CancellationToken ct)
    {
        if (request.Tasks is null || request.Tasks.Length is 0 or > 50
            || request.Tasks.Any(task => task is null)
            || request.Tasks.Select(task => task.Key).Distinct(StringComparer.Ordinal).Count() != request.Tasks.Length)
            throw new ArgumentException("Select distinct task proposals to accept.");
        var run = await db.GenerationRuns.SingleOrDefaultAsync(item => item.ProjectId == projectId
            && item.GenerationRunId == request.GenerationRunId && item.Kind == GenerationKind.Tasks
            && item.State == GenerationState.Succeeded, ct)
            ?? throw new ArgumentException("Select a successful task-creation run in this project.");
        var input = await db.GenerationInputs.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.GenerationRunId == request.GenerationRunId, ct)
            ?? throw new KeyNotFoundException();
        using var saved = JsonDocument.Parse(input.SnapshotJson);
        var preview = saved.RootElement.GetProperty("Preview").Deserialize<TaskCreationPreviewDto>()
            ?? throw new InvalidOperationException("The saved task preview is invalid.");
        var sourceRequest = saved.RootElement.GetProperty("Request").Deserialize<GenerateTaskCreationPreviewRequest>()
            ?? throw new InvalidOperationException("The saved task request is invalid.");
        var originals = preview.Tasks.ToDictionary(task => task.Key, StringComparer.Ordinal);

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var project = await db.Projects.SingleAsync(item => item.ProjectId == projectId, ct);
        // if (preview.GuidelinesRevisionId != project.CurrentGuidelinesRevisionId)
        //     throw new InvalidOperationException("Project guidelines changed. Regenerate task proposals against the current guidelines.");
        foreach (var task in request.Tasks)
        {
            if (!originals.TryGetValue(task.Key, out var original) || original.ParentPbiId != task.ParentPbiId)
                throw new ArgumentException("A task candidate does not match the saved generation preview.");
            var source = await db.Stories.SingleOrDefaultAsync(item => item.ProjectId == projectId
                && item.PbiId == task.ParentPbiId
                && (item.Type == PbiType.Story || item.Type == PbiType.Bug) && !item.IsDraft, ct)
                ?? throw new KeyNotFoundException("The selected source work item no longer exists.");
            if (source.Version != original.SourceVersion || SourceText(source) != original.SourceSnapshot
                || source.Status == PbiStatus.Done)
                throw new InvalidOperationException("The source work item changed. Review/regenerate task proposals before acceptance.");
            if (string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 200
                || string.IsNullOrWhiteSpace(task.Description) || task.Description.Length > 10000)
                throw new ArgumentException("Task titles must be 1-200 characters and task descriptions 1-10,000 characters.");
            if (task.DependsOnKeys is null || task.ExistingPrerequisites is null
                || task.DependsOnKeys.Distinct(StringComparer.Ordinal).Count() != task.DependsOnKeys.Length)
                throw new ArgumentException("Use distinct task dependency keys.");
        }

        var alreadyAccepted = await db.Stories.Where(item => item.ProjectId == projectId
            && item.GenerationRunId == request.GenerationRunId && item.GenerationCandidateKey != null).ToListAsync(ct);
        var acceptedByKey = alreadyAccepted.ToDictionary(item => item.GenerationCandidateKey!, StringComparer.Ordinal);
        var existingGenerated = await db.Stories.Where(item => item.ProjectId == projectId
            && item.Type == PbiType.Task && item.Origin == PbiOrigin.AiGenerated
            && item.GenerationRunId != request.GenerationRunId
            && item.ParentStoryId.HasValue && sourceRequest.ParentPbiIds.Contains(item.ParentStoryId.Value)).ToListAsync(ct);
        if (sourceRequest.ReplaceGeneratedTasks != (existingGenerated.Count > 0) && alreadyAccepted.Count == 0)
            throw new InvalidOperationException("Generated child tasks changed since preview; reload and regenerate.");
        if (alreadyAccepted.Count > 0 && existingGenerated.Count > 0)
            throw new InvalidOperationException("Other generated child tasks changed after partial acceptance; regenerate the complete source task set.");
        var replacedGeneratedIds = sourceRequest.ReplaceGeneratedTasks
            ? existingGenerated.Select(item => item.PbiId).ToHashSet()
            : new HashSet<int>();
        foreach (var task in request.Tasks)
        {
            foreach (var id in task.ExistingPrerequisites.Distinct())
            {
                if (replacedGeneratedIds.Contains(id)
                    || !await db.Stories.AnyAsync(existing => existing.ProjectId == projectId
                        && existing.PbiId == id && existing.Type == PbiType.Task, ct))
                    throw new ArgumentException("Existing prerequisites must be tasks in this project that are not being replaced.");
            }
        }
        if (sourceRequest.ReplaceGeneratedTasks && alreadyAccepted.Count == 0)
            await dependencies.DeleteGeneratedChildTasksAsync(projectId, sourceRequest.ParentPbiIds, ct);

        var currentGuidelines = await db.ProjectGuidelinesRevisions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.ProjectGuidelinesRevisionId == project.CurrentGuidelinesRevisionId, ct);
        var added = new Dictionary<string, ProductBacklogItem>(StringComparer.Ordinal);
        foreach (var task in request.Tasks)
        {
            if (acceptedByKey.ContainsKey(task.Key)) continue;
            var original = originals[task.Key];
            var source = await db.Stories.SingleAsync(item => item.ProjectId == projectId
                && item.PbiId == task.ParentPbiId, ct);
            var item = new ProductBacklogItem
            {
                ProjectId = projectId,
                ParentStoryId = source.PbiId,
                SprintId = source.SprintId,
                Title = task.Title,
                Description = TaskCreationSpecificationFormatter.Format(task.Title, task.Description, source, currentGuidelines?.Content),
                GenerationRunId = run.GenerationRunId,
                GenerationCandidateKey = task.Key,
                Type = PbiType.Task,
                Origin = PbiOrigin.AiGenerated,
                IsDraft = request.AsDraft,
                Status = PbiStatus.ToDo
            };
            if (source.Version != original.SourceVersion || SourceText(source) != original.SourceSnapshot)
                throw new InvalidOperationException("The source work item changed before the task could be saved.");
            db.Stories.Add(item);
            added.Add(task.Key, item);
        }

        if (added.Count > 0) await dependencies.SaveAsync(ct);
        foreach (var task in request.Tasks)
        {
            if (acceptedByKey.TryGetValue(task.Key, out var previouslyAccepted))
            {
                added[task.Key] = previouslyAccepted;
                continue;
            }
            var item = added[task.Key];
            foreach (var key in task.DependsOnKeys)
            {
                if (!added.TryGetValue(key, out var prerequisite))
                    throw new ArgumentException("Accept required task proposals before their dependents.");
                db.PbiDependencies.Add(new()
                {
                    ProjectId = projectId,
                    DependentPbiId = item.PbiId,
                    PrerequisitePbiId = prerequisite.PbiId,
                    ActorId = actor
                });
            }
            foreach (var id in task.ExistingPrerequisites.Distinct())
            {
                if (!await db.Stories.AnyAsync(existing => existing.ProjectId == projectId
                    && existing.PbiId == id && existing.Type == PbiType.Task, ct))
                    throw new ArgumentException("Existing prerequisites must be tasks in this project.");
                db.PbiDependencies.Add(new()
                {
                    ProjectId = projectId,
                    DependentPbiId = item.PbiId,
                    PrerequisitePbiId = id,
                    ActorId = actor
                });
            }
        }

        await db.SaveChangesAsync(ct);
        var allItems = await db.Stories.Where(item => item.ProjectId == projectId).ToListAsync(ct);
        var allEdges = await db.PbiDependencies.Where(item => item.ProjectId == projectId).ToListAsync(ct);
        if (DependencyGraphEvaluator.Evaluate(allItems,
                allEdges.Select(item => new DependencyLink(item.DependentPbiId, item.PrerequisitePbiId))).HasCycle)
            throw new ArgumentException("The accepted task dependencies create an execution cycle.");
        var graph = await db.ProjectGraphStates.Where(item => item.ProjectId == projectId)
            .Select(item => item.Revision).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        await notifier.NotifyAsync(projectId, graph, ct);
        return request.Tasks.Select(task => added[task.Key]).ToArray();
    }

    public async Task<ProductBacklogItem> CreateManualAsync(int projectId,
        CreateManualChildTaskRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200
            || request.Description is null || request.Description.Length > 10000)
            throw new ArgumentException("Task titles must be 1-200 characters and descriptions at most 10,000 characters.");
        var source = await db.Stories.SingleOrDefaultAsync(item => item.ProjectId == projectId
            && item.PbiId == request.ParentPbiId
            && (item.Type == PbiType.Story || item.Type == PbiType.Bug) && !item.IsDraft, ct)
            ?? throw new KeyNotFoundException("The committed source story or bug was not found.");
        if (source.Status == PbiStatus.Done)
            throw new InvalidOperationException("Reopen completed source work before adding child tasks.");
        var task = new ProductBacklogItem
        {
            ProjectId = projectId,
            ParentStoryId = source.PbiId,
            SprintId = source.SprintId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Type = PbiType.Task,
            Origin = PbiOrigin.WebUserCreated,
            IsDraft = true,
            Status = PbiStatus.ToDo
        };
        db.Stories.Add(task);
        await dependencies.SaveAsync(ct);
        return task;
    }

    public Task DeleteAsync(int projectId, int taskId, CancellationToken ct) =>
        dependencies.DeleteChildTaskAsync(projectId, taskId, ct);

    private static string SourceText(ProductBacklogItem source) =>
        $"{source.Type}\n{source.Title}\n{source.Description}";

    private static string Fingerprint(GenerateTaskCreationPreviewRequest request) =>
        JsonSerializer.Serialize(new
        {
            ParentPbiIds = request.ParentPbiIds.Order(),
            request.ReplaceGeneratedTasks
        });
}
