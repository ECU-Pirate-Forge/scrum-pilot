using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScrumPilot.API.Configuration;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class ProjectDependencyService(
    OrganizationContext db, ProjectWorkNotifier notifier, GitHubRepositoryRegistry registry,
    ScrumPilot.API.Authorization.ICurrentUser? currentUser = null,
    PullRequestScopeService? scope = null,
    IOptions<GitHubIntegrationOptions>? githubOptions = null) : IPlanningMutationHandler
{
    public async Task<DependencyGraphDto> ReadAsync(int projectId, int? sprintId, CancellationToken ct)
    {
        var items = await db.Stories.AsNoTracking().Where(item => item.ProjectId == projectId).ToListAsync(ct);
        var links = await LinksAsync(projectId, items, ct);
        var reviews = await db.DependencyReviews.AsNoTracking().Where(review =>
            review.ProjectId == projectId && review.ReviewedRevision != review.InvalidationRevision).ToListAsync(ct);
        var project = await db.Projects.AsNoTracking().SingleAsync(project => project.ProjectId == projectId, ct);
        var specs = await db.TaskSpecificationRevisions.AsNoTracking().Where(spec => spec.ProjectId == projectId).ToListAsync(ct);
        var sourceCurrency = await ProjectStoryCurrency.ReadManyAsync(db, projectId,
            items.Where(item => item.Type == PbiType.Story).Select(item => item.PbiId).ToArray(), ct);
        var currentRepositoryIds = new HashSet<int>();
        foreach (var repository in await db.ProjectRepositories.AsNoTracking().Where(x => x.ProjectId == projectId).ToListAsync(ct))
            if (await registry.IsCurrentAsync(repository, ct)) currentRepositoryIds.Add(repository.ProjectRepositoryId);
        var currentSnapshotIds = await db.RepositorySnapshots.AsNoTracking().Where(x =>
            x.ProjectId == projectId && currentRepositoryIds.Contains(x.ProjectRepositoryId))
            .Select(x => x.RepositorySnapshotId).ToListAsync(ct);
        var approved = specs.GroupBy(spec => spec.PbiId).Select(group => group.MaxBy(spec => spec.Revision)!)
            .Where(spec => spec.Approval == SpecificationApproval.Approved && spec.RepositorySnapshotId.HasValue
                && currentSnapshotIds.Contains(spec.RepositorySnapshotId.Value)
                && spec.ProjectGuidelinesRevisionId == project.CurrentGuidelinesRevisionId
                && items.Any(task => task.PbiId == spec.PbiId && items.Any(source =>
                    source.PbiId == (task.ParentStoryId ?? task.PbiId)
                    && spec.Content.SourceStorySnapshot == TaskSpecificationService.SourceText(source)
                    && (source.Type != PbiType.Story || sourceCurrency.TryGetValue(source.PbiId, out var currency)
                        && !currency.SourceChanged && !currency.GuidelinesChanged && !currency.SourceArchived && !currency.ContentChanged))))
            .Select(spec => spec.PbiId).ToHashSet();
        var evaluated = DependencyGraphEvaluator.Evaluate(items, links, reviews.Select(review => review.PbiId).ToHashSet(), approved);
        var selected = items.Where(item => !item.IsDraft && (!sprintId.HasValue || item.SprintId == sprintId)).Select(item => item.PbiId).ToHashSet();
        var queue = new Queue<int>(selected);
        while (queue.TryDequeue(out var id))
        {
            if (!evaluated.CompletionRequirements.TryGetValue(id, out var prerequisites)) continue;
            foreach (var prerequisite in prerequisites)
                if (selected.Add(prerequisite)) queue.Enqueue(prerequisite);
        }
        var visible = items.Where(item => selected.Contains(item.PbiId)).ToArray();
        var revision = await db.ProjectGraphStates.Where(state => state.ProjectId == projectId)
            .Select(state => state.Revision).SingleOrDefaultAsync(ct);
        var edges = links.Where(link => selected.Contains(link.DependentId)).Distinct().ToArray();
        return new(projectId, sprintId, revision, visible.Select(item =>
        {
            var state = evaluated.Nodes[item.PbiId];
            return new DependencyGraphNodeDto(item, state.State, state.CompletionSatisfied, state.DependencyReady,
                state.ImplementationEligible, sprintId.HasValue && item.SprintId != sprintId,
                state.BlockingIds.ToArray(), state.Reasons.ToArray());
        }).ToArray(), edges.Select(edge => new DependencyGraphEdgeDto(edge.DependentId, edge.PrerequisiteId)).ToArray(),
            DependencyMermaidWriter.Write(visible, edges, evaluated, sprintId, revision),
            evaluated.HasCycle ? ["Legacy dependency cycles require repair."] : []);
    }

    public async Task SetAsync(int projectId, SetDependenciesRequest request, CancellationToken ct)
    {
        if (request.PrerequisiteIds.Distinct().Count() != request.PrerequisiteIds.Length)
            throw new ArgumentException("Duplicate prerequisites are not permitted.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var state = await TouchAsync(projectId, request.ExpectedRevision, ct);
        var items = await db.Stories.Where(item => item.ProjectId == projectId).ToListAsync(ct);
        var dependent = items.SingleOrDefault(item => item.PbiId == request.DependentId)
            ?? throw new KeyNotFoundException();
        var targets = items.Where(item => request.PrerequisiteIds.Contains(item.PbiId)).ToArray();
        if (targets.Length != request.PrerequisiteIds.Length || targets.Any(item => item.PbiId == dependent.PbiId))
            throw new ArgumentException("Every prerequisite must be a different item in the same project.");
        if (targets.Any(item => (dependent.Type == PbiType.Story) != (item.Type == PbiType.Story)))
            throw new ArgumentException("Use story-to-story or task-to-task dependencies; containment is not a dependency.");
        var previous = await db.PbiDependencies.Where(edge => edge.ProjectId == projectId && edge.DependentPbiId == dependent.PbiId).ToListAsync(ct);
        var oldLinks = await LinksAsync(projectId, items, ct);
        var links = oldLinks.Where(link => link.DependentId != dependent.PbiId)
            .Concat(request.PrerequisiteIds.Select(id => new DependencyLink(dependent.PbiId, id))).ToArray();
        var evaluation = DependencyGraphEvaluator.Evaluate(items, links);
        if (evaluation.HasCycle) throw new ArgumentException("This change creates an effective dependency cycle, including story rollups.");
        db.PbiDependencies.RemoveRange(previous.Where(edge => !request.PrerequisiteIds.Contains(edge.PrerequisitePbiId)));
        foreach (var id in request.PrerequisiteIds.Where(id => previous.All(edge => edge.PrerequisitePbiId != id)))
            db.PbiDependencies.Add(new() { ProjectId = projectId, DependentPbiId = dependent.PbiId, PrerequisitePbiId = id, ActorId = currentUser?.UserId ?? "" });
        dependent.DependsOnPbiId = request.PrerequisiteIds.Order().Cast<int?>().FirstOrDefault();
        await InvalidateAsync(projectId, items, evaluation, [dependent.PbiId], state.Revision + 1, ct);
        await db.SaveChangesAsync(ct);
        await EnsureFirstRevisionAsync(state, ct);
        await transaction.CommitAsync(ct);
        await notifier.NotifyAsync(projectId, state.Revision, ct);
    }

    public async Task DeleteChildTaskAsync(int projectId, int taskId, CancellationToken ct)
    {
        await DeleteTasksAsync(projectId, [taskId], ct);
    }

    public async Task<int[]> DeleteGeneratedChildTasksAsync(int projectId, int[] parentPbiIds, CancellationToken ct)
    {
        var taskIds = await db.Stories.AsNoTracking().Where(item => item.ProjectId == projectId
            && item.Type == PbiType.Task && item.Origin == PbiOrigin.AiGenerated
            && item.ParentStoryId.HasValue && parentPbiIds.Contains(item.ParentStoryId.Value))
            .Select(item => item.PbiId).ToArrayAsync(ct);
        if (taskIds.Length == 0) return [];
        await DeleteTasksAsync(projectId, taskIds, ct);
        return taskIds;
    }

    public async Task ReviewAsync(int projectId, ReviewDependencyRequest request, string actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var graph = await TouchAsync(projectId, null, ct);
        var review = await db.DependencyReviews.SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.PbiId == request.PbiId, ct) ?? throw new KeyNotFoundException();
        if (review.InvalidationRevision != request.InvalidationRevision)
            throw new InvalidOperationException("A newer invalidation exists. Reload before reviewing.");
        var items = await db.Stories.Where(item => item.ProjectId == projectId).ToListAsync(ct);
        var pending = await db.DependencyReviews.Where(item => item.ProjectId == projectId
            && item.PbiId != request.PbiId && item.ReviewedRevision != item.InvalidationRevision).Select(item => item.PbiId).ToListAsync(ct);
        var evaluation = DependencyGraphEvaluator.Evaluate(items, await LinksAsync(projectId, items, ct), pending.ToHashSet());
        var item = items.Single(item => item.PbiId == request.PbiId);
        var required = evaluation.CompletionRequirements[request.PbiId];
        if (evaluation.HasCycle || required.Any(id => !evaluation.Nodes.TryGetValue(id, out var prerequisite) || !prerequisite.CompletionSatisfied))
            throw new InvalidOperationException("Resolve all prerequisites and child work before acknowledging acceptance.");
        review.ReviewedRevision = review.InvalidationRevision;
        review.ReviewedByActorId = actor;
        review.ReviewedAt = DateTime.UtcNow;
        db.DependencyReviewEvents.Add(new()
        {
            ProjectId = projectId,
            PbiId = request.PbiId,
            InvalidationRevision = review.InvalidationRevision,
            Accepted = true,
            ActorId = actor,
            Reason = "Acceptance criteria reviewed after prerequisite resolution."
        });
        await db.SaveChangesAsync(ct);
        await EnsureFirstRevisionAsync(graph, ct);
        await transaction.CommitAsync(ct);
        await notifier.NotifyAsync(projectId, graph.Revision, ct);
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        var changedSources = db.ChangeTracker.Entries<ProductBacklogItem>().Where(entry =>
                entry.State == EntityState.Modified && entry.Properties.Any(property =>
                    property.Metadata.Name is nameof(ProductBacklogItem.Title) or nameof(ProductBacklogItem.Description)
                    && !Equals(property.OriginalValue, property.CurrentValue))).Select(entry => entry.Entity.PbiId).ToArray();
        var changes = db.ChangeTracker.Entries<ProductBacklogItem>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => new WorkChange(entry.Entity, entry.State,
                entry.State == EntityState.Added ? null : entry.Property(item => item.DependsOnPbiId).OriginalValue,
                entry.State == EntityState.Added ? null : entry.Property(item => item.ParentStoryId).OriginalValue,
                entry.State == EntityState.Added ? PbiStatus.ToDo : entry.Property(item => item.Status).OriginalValue,
                entry.State == EntityState.Added ? null : entry.Property(item => item.SprintId).OriginalValue,
                entry.State == EntityState.Added || entry.Property(item => item.IsDraft).OriginalValue)).ToArray();
        if (changes.Length == 0) { await db.SaveChangesAsync(ct); return; }
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var projectStates = new List<ProjectGraphState>();
        foreach (var projectId in changes.Select(change => change.Item.ProjectId).Distinct().Order())
        {
            await db.AcquirePlanningPolicyLockAsync(projectId, ct);
            if (scope is not null && githubOptions?.Value.Enabled != false)
                foreach (var task in await db.Stories.Where(item => item.ProjectId == projectId && item.Type == PbiType.Task
                             && (changedSources.Contains(item.PbiId) || item.ParentStoryId.HasValue && changedSources.Contains(item.ParentStoryId.Value))).ToListAsync(ct))
                    await scope.InvalidateTaskAsync(projectId, task.PbiId, ct);
            var graph = await TouchAsync(projectId, null, ct);
            projectStates.Add(graph);
            foreach (var change in changes.Where(change => change.Item.ProjectId == projectId))
            {
                if (change.State == EntityState.Deleted
                    && (await db.PbiDependencies.AnyAsync(edge => edge.ProjectId == projectId &&
                            (edge.PrerequisitePbiId == change.Item.PbiId || edge.DependentPbiId == change.Item.PbiId), ct)
                        || await db.TaskSpecificationRevisions.AnyAsync(spec => spec.ProjectId == projectId && spec.PbiId == change.Item.PbiId, ct)
                        || await db.DependencyReviewEvents.AnyAsync(review => review.ProjectId == projectId && review.PbiId == change.Item.PbiId, ct)
                        || await db.PlanningArtifactReviews.AnyAsync(review => review.ProjectId == projectId && review.PbiId == change.Item.PbiId, ct)
                        || await db.Stories.AnyAsync(item => item.ProjectId == projectId &&
                            (item.ParentStoryId == change.Item.PbiId || item.DependsOnPbiId == change.Item.PbiId), ct)))
                    throw new InvalidOperationException("Referenced work cannot be deleted. Repair dependencies or child relationships explicitly.");
                if (change.Item.ParentStoryId is int parentId)
                {
                    var parent = await db.Stories.SingleOrDefaultAsync(item => item.ProjectId == projectId && item.PbiId == parentId, ct);
                    if (parent is null || parent.Type is not (PbiType.Story or PbiType.Bug) || change.Item.Type != PbiType.Task)
                        throw new ArgumentException("A child task must reference a story or bug in its own project.");
                    if (parent.Status == PbiStatus.Done && !change.Item.IsDraft
                        && (change.State == EntityState.Added || change.OldDraft))
                        throw new InvalidOperationException("Reopen the parent story before adding committed child work.");
                    if (change.State == EntityState.Modified && change.Item.SprintId != change.OldSprintId
                        && change.Item.SprintId != parent.SprintId)
                        throw new InvalidOperationException("Move the parent story; child sprint assignment is inherited.");
                    change.Item.SprintId = parent.SprintId;
                }
                if (change.Item.Type is PbiType.Story or PbiType.Bug && change.Item.SprintId != change.OldSprintId)
                {
                    var children = await db.Stories.Where(item => item.ProjectId == projectId && item.ParentStoryId == change.Item.PbiId).ToListAsync(ct);
                    foreach (var child in children) child.SprintId = change.Item.SprintId;
                }
                if (change.State != EntityState.Deleted && change.Item.DependsOnPbiId != change.OldDependency)
                {
                    var edges = await db.PbiDependencies.Where(edge => edge.ProjectId == projectId
                        && edge.DependentPbiId == change.Item.PbiId).ToListAsync(ct);
                    if (edges.Count > 1) throw new InvalidOperationException("Use the multiple-dependency editor; this legacy edit would erase prerequisites.");
                    db.PbiDependencies.RemoveRange(edges);
                    if (change.Item.DependsOnPbiId is int prerequisite)
                    {
                        if (prerequisite == change.Item.PbiId || !await db.Stories.AnyAsync(item =>
                            item.ProjectId == projectId && item.PbiId == prerequisite, ct))
                            throw new ArgumentException("The prerequisite must be a different item in this project.");
                        // New PBIs need their generated key before inserting the edge.
                    }
                }
            }
        }
        await db.SaveChangesAsync(ct);
        foreach (var state in projectStates)
        {
            await EnsureFirstRevisionAsync(state, ct);
            var projectChanges = changes.Where(change => change.Item.ProjectId == state.ProjectId).ToArray();
            foreach (var change in projectChanges.Where(change => change.State != EntityState.Deleted
                         && change.Item.DependsOnPbiId.HasValue && change.Item.DependsOnPbiId != change.OldDependency))
                db.PbiDependencies.Add(new()
                {
                    ProjectId = state.ProjectId,
                    DependentPbiId = change.Item.PbiId,
                    PrerequisitePbiId = change.Item.DependsOnPbiId!.Value,
                    ActorId = currentUser?.UserId ?? ""
                });
            await db.SaveChangesAsync(ct);
            var items = await db.Stories.Where(item => item.ProjectId == state.ProjectId).ToListAsync(ct);
            if (items.Any(item => item.ParentStoryId.HasValue && (item.Type != PbiType.Task
                || !items.Any(parent => parent.PbiId == item.ParentStoryId
                    && parent.Type is PbiType.Story or PbiType.Bug))))
                throw new InvalidOperationException("Parent relationships must remain task-to-story-or-bug.");
            var links = await LinksAsync(state.ProjectId, items, ct);
            var reviews = await db.DependencyReviews.Where(review => review.ProjectId == state.ProjectId
                && review.ReviewedRevision != review.InvalidationRevision).Select(review => review.PbiId).ToListAsync(ct);
            var evaluation = DependencyGraphEvaluator.Evaluate(items, links, reviews.ToHashSet());
            if (evaluation.HasCycle) throw new InvalidOperationException("The effective execution graph contains a cycle.");
            foreach (var change in projectChanges.Where(change => change.State != EntityState.Deleted
                         && change.Item.Status == PbiStatus.Done && (change.OldStatus != PbiStatus.Done || change.State == EntityState.Added)))
            {
                if (!evaluation.Nodes[change.Item.PbiId].CompletionSatisfied)
                    throw new InvalidOperationException("Resolve prerequisites, child tasks, and acceptance reviews before closing this item.");
            }
            var invalidating = projectChanges.Where(change => change.State != EntityState.Deleted &&
                (change.OldStatus == PbiStatus.Done && change.Item.Status != PbiStatus.Done
                 || change.Item.DependsOnPbiId != change.OldDependency || change.Item.ParentStoryId != change.OldParent))
                .Select(change => change.Item.PbiId).ToArray();
            if (invalidating.Length > 0)
            {
                var reopened = projectChanges.Where(change => change.OldStatus == PbiStatus.Done && change.Item.Status != PbiStatus.Done)
                    .Select(change => change.Item.PbiId).ToHashSet();
                await InvalidateAsync(state.ProjectId, items, evaluation, invalidating, state.Revision, ct, reopened);
            }
            await db.SaveChangesAsync(ct);
        }
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            foreach (var state in projectStates) await notifier.NotifyAsync(state.ProjectId, state.Revision, ct);
        }
    }

    private async Task<ProjectGraphState> TouchAsync(int projectId, long? expected, CancellationToken ct)
    {
        var state = await db.ProjectGraphStates.SingleOrDefaultAsync(state => state.ProjectId == projectId, ct);
        if (state is null)
        {
            if (expected is > 0) throw new InvalidOperationException("Graph revision changed. Reload and retry.");
            state = new() { ProjectId = projectId };
            db.ProjectGraphStates.Add(state);
        }
        else
        {
            if (expected.HasValue && expected != state.Revision) throw new InvalidOperationException("Graph revision changed. Reload and retry.");
            db.Entry(state).Property(item => item.Revision).IsModified = true;
        }
        return state;
    }

    private async Task DeleteTasksAsync(int projectId, int[] taskIds, CancellationToken ct)
    {
        if (taskIds.Length == 0) return;
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var tasks = await db.Stories.Where(item => item.ProjectId == projectId
            && taskIds.Contains(item.PbiId) && item.Type == PbiType.Task && item.ParentStoryId.HasValue).ToListAsync(ct);
        if (tasks.Count != taskIds.Distinct().Count())
            throw new KeyNotFoundException("A selected child task was not found in this project.");

        var items = await db.Stories.Where(item => item.ProjectId == projectId).ToListAsync(ct);
        var evaluation = DependencyGraphEvaluator.Evaluate(items, await LinksAsync(projectId, items, ct));
        var graph = await TouchAsync(projectId, null, ct);
        foreach (var dependent in items.Where(item => !taskIds.Contains(item.PbiId)
                     && item.DependsOnPbiId.HasValue && taskIds.Contains(item.DependsOnPbiId.Value)))
            dependent.DependsOnPbiId = null;
        await db.SaveChangesAsync(ct);
        await EnsureFirstRevisionAsync(graph, ct);

        var runIds = tasks.Where(item => item.GenerationRunId.HasValue)
            .Select(item => item.GenerationRunId!.Value).Distinct().ToArray();
        foreach (var run in await db.GenerationRuns.Where(item =>
                     item.ProjectId == projectId && runIds.Contains(item.GenerationRunId)).ToListAsync(ct))
        {
            run.State = GenerationState.Cancelled;
            run.CompletedAt = DateTime.UtcNow;
        }

        await db.PurgeTaskPlanningAsync(projectId, taskIds, ct);
        db.Stories.RemoveRange(tasks);
        await db.SaveChangesAsync(ct);
        await InvalidateAsync(projectId, items, evaluation, taskIds, graph.Revision, ct, taskIds.ToHashSet());
        await db.SaveChangesAsync(ct);

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            await notifier.NotifyAsync(projectId, graph.Revision, ct);
        }
    }

    private async Task EnsureFirstRevisionAsync(ProjectGraphState state, CancellationToken ct)
    {
        if (state.Revision != 0) return;
        db.Entry(state).Property(item => item.Revision).IsModified = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task InvalidateAsync(int projectId, List<ProductBacklogItem> items, DependencyEvaluation evaluation,
        IEnumerable<int> sources, long revision, CancellationToken ct, HashSet<int>? excluded = null)
    {
        var affected = DependencyGraphEvaluator.DependentClosure(evaluation.CompletionRequirements, sources);
        foreach (var item in items.Where(item => affected.Contains(item.PbiId)
                     && excluded?.Contains(item.PbiId) != true && item.Status is PbiStatus.InProgress or PbiStatus.InReview or PbiStatus.Done))
        {
            var review = await db.DependencyReviews.SingleOrDefaultAsync(review => review.ProjectId == projectId && review.PbiId == item.PbiId, ct);
            if (review is null)
            {
                review = new() { ProjectId = projectId, PbiId = item.PbiId };
                db.DependencyReviews.Add(review);
            }
            review.InvalidationRevision = revision;
            review.ReviewedRevision = null;
            db.DependencyReviewEvents.Add(new()
            {
                ProjectId = projectId,
                PbiId = item.PbiId,
                InvalidationRevision = revision,
                ActorId = currentUser?.UserId ?? "",
                Reason = "Upstream completion or dependency structure changed; acceptance requires review."
            });
        }
    }

    private async Task<List<DependencyLink>> LinksAsync(int projectId, IReadOnlyCollection<ProductBacklogItem> items, CancellationToken ct)
    {
        var edges = await db.PbiDependencies.AsNoTracking().Where(edge => edge.ProjectId == projectId).ToListAsync(ct);
        return edges.Select(edge => new DependencyLink(edge.DependentPbiId, edge.PrerequisitePbiId))
            .Concat(items.Where(item => item.DependsOnPbiId.HasValue)
                .Select(item => new DependencyLink(item.PbiId, item.DependsOnPbiId!.Value))).Distinct().ToList();
    }

    private sealed record WorkChange(ProductBacklogItem Item, EntityState State, int? OldDependency,
        int? OldParent, PbiStatus OldStatus, int? OldSprintId, bool OldDraft);
}
