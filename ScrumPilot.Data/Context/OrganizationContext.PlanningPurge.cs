using Microsoft.EntityFrameworkCore;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context;

internal sealed class PlanningPurgeScope
{
    public int ProjectId { get; set; }
    public long TransactionId { get; set; }
}

public sealed partial class OrganizationContext
{
    /// <summary>Deletes planning artifacts owned by one task inside an explicit task-removal transaction.</summary>
    public async Task PurgeTaskPlanningAsync(int projectId, IReadOnlyCollection<int> taskIds, CancellationToken ct)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Task planning purge requires an enclosing deletion transaction.");
        if (taskIds.Count == 0) return;

        await AcquirePlanningPolicyLockAsync(projectId, ct);
        await AcquireProjectLockAsync("planning-purge", projectId, ct);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO [PlanningPurgeScopes] ([ProjectId], [TransactionId]) VALUES ({projectId}, CURRENT_TRANSACTION_ID())""", ct);

        var bindingIds = await TaskPullRequestBindings.Where(item =>
            item.ProjectId == projectId && taskIds.Contains(item.PbiId))
            .Select(item => item.TaskPullRequestBindingId).ToArrayAsync(ct);
        var revisionIds = await TaskSpecificationRevisions.Where(item =>
            item.ProjectId == projectId && taskIds.Contains(item.PbiId))
            .Select(item => item.TaskSpecificationRevisionId).ToArrayAsync(ct);

        await PullRequestScopeChecks.Where(item => item.ProjectId == projectId
            && bindingIds.Contains(item.TaskPullRequestBindingId)).ExecuteDeleteAsync(ct);
        await TaskAllowedFiles.Where(item => item.ProjectId == projectId
            && revisionIds.Contains(item.TaskSpecificationRevisionId)).ExecuteDeleteAsync(ct);
        await TaskSpecificationRevisions.Where(item => item.ProjectId == projectId
            && taskIds.Contains(item.PbiId)).ExecuteDeleteAsync(ct);
        await TaskPullRequestBindings.Where(item => item.ProjectId == projectId
            && taskIds.Contains(item.PbiId)).ExecuteDeleteAsync(ct);
        await DependencyReviewEvents.Where(item => item.ProjectId == projectId
            && taskIds.Contains(item.PbiId)).ExecuteDeleteAsync(ct);
        await PlanningArtifactReviews.Where(item => item.ProjectId == projectId
            && taskIds.Contains(item.PbiId)).ExecuteDeleteAsync(ct);
        await DependencyReviews.Where(item => item.ProjectId == projectId
            && taskIds.Contains(item.PbiId)).ExecuteDeleteAsync(ct);
        await PbiDependencies.Where(item => item.ProjectId == projectId
            && (taskIds.Contains(item.DependentPbiId) || taskIds.Contains(item.PrerequisitePbiId))).ExecuteDeleteAsync(ct);

        await Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "PlanningPurgeScopes" WHERE "ProjectId" = {projectId}""", ct);
        foreach (var entry in ChangeTracker.Entries().Where(entry =>
                     entry.Entity is TaskAllowedFile file && file.ProjectId == projectId && revisionIds.Contains(file.TaskSpecificationRevisionId)
                     || entry.Entity is TaskSpecificationRevision specification && specification.ProjectId == projectId && taskIds.Contains(specification.PbiId)
                     || entry.Entity is TaskPullRequestBinding binding && binding.ProjectId == projectId && taskIds.Contains(binding.PbiId)
                     || entry.Entity is PullRequestScopeCheck check && check.ProjectId == projectId && bindingIds.Contains(check.TaskPullRequestBindingId)
                     || entry.Entity is PbiDependency dependency && dependency.ProjectId == projectId
                         && (taskIds.Contains(dependency.DependentPbiId) || taskIds.Contains(dependency.PrerequisitePbiId))
                     || entry.Entity is DependencyReviewEvent reviewEvent && reviewEvent.ProjectId == projectId && taskIds.Contains(reviewEvent.PbiId)
                     || entry.Entity is PlanningArtifactReview artifactReview && artifactReview.ProjectId == projectId && taskIds.Contains(artifactReview.PbiId)
                     || entry.Entity is DependencyReview review && review.ProjectId == projectId && taskIds.Contains(review.PbiId)).ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>Used only by the authorized project-delete and retention-controlled organization-purge transactions.</summary>
    public async Task PurgeProjectPlanningAsync(int projectId, CancellationToken ct)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Planning purge requires an enclosing deletion transaction.");

        await AcquirePlanningPolicyLockAsync(projectId, ct);
        await AcquireProjectLockAsync("planning-purge", projectId, ct);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO [PlanningPurgeScopes] ([ProjectId], [TransactionId]) VALUES ({projectId}, CURRENT_TRANSACTION_ID())""", ct);
        await Projects.Where(item => item.ProjectId == projectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.CurrentGuidelinesRevisionId, (int?)null), ct);
        await Stories.Where(item => item.ProjectId == projectId).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.ParentStoryId, (int?)null)
            .SetProperty(item => item.ProblemStatementId, (int?)null)
            .SetProperty(item => item.GenerationRunId, (int?)null), ct);
        await PullRequestScopeChecks.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await PlanningArtifactReviews.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await TaskPullRequestBindings.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await NonTaskPullRequestExceptions.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await TaskAllowedFiles.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await TaskSpecificationRevisions.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await GenerationInputs.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await GenerationRuns.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await RepositorySnapshots.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await ProjectRepositories.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await ProblemStatements.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await ProjectGuidelinesRevisions.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await PbiDependencies.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await DependencyReviewEvents.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await DependencyReviews.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await ProjectGraphStates.Where(item => item.ProjectId == projectId).ExecuteDeleteAsync(ct);
        await Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "PlanningPurgeScopes" WHERE "ProjectId" = {projectId}""", ct);
        foreach (var entry in ChangeTracker.Entries().Where(entry =>
                     entry.Metadata.FindProperty("ProjectId") is not null
                     && Equals(entry.Property("ProjectId").CurrentValue, projectId)).ToList())
        {
            if (entry.Entity is Project)
                entry.Property(nameof(Project.CurrentGuidelinesRevisionId)).CurrentValue = null;
            else if (entry.Entity is ProductBacklogItem)
            {
                foreach (var name in new[] { nameof(ProductBacklogItem.ParentStoryId),
                             nameof(ProductBacklogItem.ProblemStatementId), nameof(ProductBacklogItem.GenerationRunId) })
                {
                    entry.Property(name).CurrentValue = null;
                    entry.Property(name).OriginalValue = null;
                }
            }
            else if (entry.Entity is ProjectGuidelinesRevision or ProblemStatement or GenerationRun or GenerationInput
                     or ProjectRepository or RepositorySnapshot or TaskSpecificationRevision or TaskAllowedFile
                     or PbiDependency or DependencyReview or DependencyReviewEvent or ProjectGraphState
                     or TaskPullRequestBinding or PullRequestScopeCheck or NonTaskPullRequestException or PlanningArtifactReview)
                entry.State = EntityState.Detached;
        }
    }
}
