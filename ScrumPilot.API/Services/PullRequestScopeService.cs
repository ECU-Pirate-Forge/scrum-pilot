using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class PullRequestScopeService(OrganizationContext db, GitHubAppClient github, TaskSpecificationService specs,
    ControlPlaneContext control, GitHubRepositoryRegistry registry)
{
    public async Task<PullRequestScopeCheck> BindAsync(int projectId, int taskId, int number, string actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var status = await specs.StatusAsync(projectId, taskId, ct);
        if (!status.IsCurrent) throw new InvalidOperationException(string.Join(" ", status.ReviewReasons));
        var repository = await db.ProjectRepositories.SingleOrDefaultAsync(item => item.ProjectId == projectId, ct)
            ?? throw new InvalidOperationException("Configure the project's GitHub App repository first.");
        await registry.RequireCurrentAsync(repository, ct);
        if (await db.NonTaskPullRequestExceptions.AnyAsync(item => item.ProjectRepositoryId == repository.ProjectRepositoryId
            && item.PullRequestNumber == number, ct)) throw new InvalidOperationException("This PR already has a non-task exception.");
        var binding = await db.TaskPullRequestBindings.SingleOrDefaultAsync(item =>
            item.ProjectRepositoryId == repository.ProjectRepositoryId && item.PullRequestNumber == number, ct);
        if (binding is not null && binding.PbiId != taskId) throw new InvalidOperationException("A PR may bind to only one task.");
        if (binding is null)
        {
            await github.GetPullRequestIdentityAsync(repository.InstallationId, repository.GitHubRepositoryId,
                repository.Owner, repository.Name, number, ct);
            binding = new()
            {
                ProjectId = projectId,
                PbiId = taskId,
                ProjectRepositoryId = repository.ProjectRepositoryId,
                PullRequestNumber = number,
                ActorId = actor
            };
            db.TaskPullRequestBindings.Add(binding);
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return await ValidateAsync(binding, ct);
    }

    public async Task<PullRequestScopeCheck> ValidateAsync(TaskPullRequestBinding binding, CancellationToken ct)
    {
        var repository = await db.ProjectRepositories.AsNoTracking().SingleAsync(item => item.ProjectRepositoryId == binding.ProjectRepositoryId
            && item.ProjectId == binding.ProjectId, ct);
        var head = await RecordAttemptAsync(repository, binding.PullRequestNumber, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(binding.ProjectId, ct);
        var result = await ValidateLockedAsync(binding, repository, head, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<PullRequestScopeCheck> ValidateLockedAsync(TaskPullRequestBinding binding,
        ProjectRepository repository, string expectedHead, CancellationToken ct)
    {
        await registry.RequireCurrentAsync(repository, ct);
        var identity = await github.GetPullRequestIdentityAsync(repository.InstallationId, repository.GitHubRepositoryId,
            repository.Owner, repository.Name, binding.PullRequestNumber, ct);
        if (identity.HeadSha != expectedHead) throw new InvalidOperationException("The PR head changed. Retry to register its new head before validation.");
        if (!identity.IsOpen) throw new InvalidOperationException("Only an open PR can receive current scope approval.");
        await github.EnsureExclusivePullRequestHeadAsync(repository.InstallationId, repository.GitHubRepositoryId,
            repository.Owner, repository.Name, binding.PullRequestNumber, identity.HeadSha, ct);
        var pr = await github.GetPullRequestAsync(repository.InstallationId, repository.GitHubRepositoryId,
            repository.Owner, repository.Name, binding.PullRequestNumber, ct);
        if (pr.HeadSha != identity.HeadSha) throw new InvalidOperationException("The PR head changed during verification. Retry.");
        var status = await specs.StatusAsync(binding.ProjectId, binding.PbiId, ct);
        var spec = status.Specification ?? throw new InvalidOperationException("The bound task has no specification.");
        var errors = new List<string>(status.ReviewReasons);
        var snapshot = await db.RepositorySnapshots.SingleOrDefaultAsync(item => item.ProjectId == binding.ProjectId
            && item.RepositorySnapshotId == spec.RepositorySnapshotId, ct);
        if (snapshot?.ProjectRepositoryId != repository.ProjectRepositoryId)
            errors.Add("The specification repository does not match the PR.");
        if (snapshot?.CommitSha != pr.MergeBaseSha)
            errors.Add("The PR merge-base differs from the approved snapshot. Review and approve scope against the current base.");
        errors.AddRange(TaskFileScope.Violations(spec.AllowedFiles.Select(TaskSpecificationService.Proposal).Select(TaskSpecificationService.Rule), pr.Files));
        var success = errors.Count == 0;
        if (success)
            await github.EnsureExclusivePullRequestHeadAsync(repository.InstallationId, repository.GitHubRepositoryId,
                repository.Owner, repository.Name, binding.PullRequestNumber, pr.HeadSha, ct);
        var summary = $"Task #{binding.PbiId}; spec revision {spec.Revision}; hash {spec.ContentHash}; base {pr.BaseSha}; merge-base {pr.MergeBaseSha}.\n"
            + (success ? "Every net changed file and operation is approved. This does not verify semantics or test execution." : string.Join("\n", errors));
        var checkId = await github.PublishCheckAsync(repository.InstallationId, repository.GitHubRepositoryId,
            repository.Owner, repository.Name, pr.HeadSha, success, summary, ct);
        if (success && !await registry.IsCurrentAsync(repository, ct))
        {
            await github.PublishCheckAsync(repository.InstallationId, repository.GitHubRepositoryId,
                repository.Owner, repository.Name, pr.HeadSha, false, "Repository authority changed during validation.", ct);
            throw new InvalidOperationException("Repository authority changed during validation. Reconcile before retrying.");
        }
        var check = new PullRequestScopeCheck
        {
            ProjectId = binding.ProjectId,
            TaskPullRequestBindingId = binding.TaskPullRequestBindingId,
            TaskSpecificationRevisionId = spec.TaskSpecificationRevisionId,
            RepositoryVersion = repository.Version,
            HeadSha = pr.HeadSha,
            BaseSha = pr.BaseSha,
            MergeBaseSha = pr.MergeBaseSha,
            GitHubCheckId = checkId,
            State = success ? ScopeCheckState.Passed : ScopeCheckState.Failed,
            ResultJson = JsonSerializer.Serialize(new { SpecificationHash = spec.ContentHash, Errors = errors, Enforcement = "Branch protection requires administrator verification." })
        };
        db.PullRequestScopeChecks.Add(check);
        await db.SaveChangesAsync(ct);
        return check;
    }

    public async Task InvalidateTaskAsync(int projectId, int taskId, CancellationToken ct)
    {
        var bindings = await db.TaskPullRequestBindings.Where(item => item.ProjectId == projectId && item.PbiId == taskId).ToListAsync(ct);
        foreach (var binding in bindings)
        {
            var repository = await db.ProjectRepositories.SingleAsync(item => item.ProjectRepositoryId == binding.ProjectRepositoryId, ct);
            if (!github.IntegrationEnabled)
            {
                await registry.SuspendWhileDisabledAsync(repository, ct);
                continue;
            }
            const string pending = "Task inputs or scope are changing. Revalidate the approved specification before merging.";
            var invalidatedHeads = await InvalidateKnownHeadsAsync(repository, binding.PullRequestNumber, pending, ct);
            var pr = await github.GetPullRequestIdentityAsync(repository.InstallationId, repository.GitHubRepositoryId,
                repository.Owner, repository.Name, binding.PullRequestNumber, ct);
            if (!invalidatedHeads.Contains(pr.HeadSha))
                await github.PublishCheckAsync(repository.InstallationId, repository.GitHubRepositoryId,
                    repository.Owner, repository.Name, pr.HeadSha, false, pending, ct);
        }
    }

    private async Task<HashSet<string>> InvalidateKnownHeadsAsync(ProjectRepository repository, int number, string reason, CancellationToken ct)
    {
        var registration = await registry.RequireOwnedAsync(repository, ct);
        var attempts = await control.PullRequestScopeAttempts.AsNoTracking().Where(item =>
            item.RegistrationId == registration.RegistrationId && item.PullRequestNumber == number)
            .Select(item => item.HeadSha).ToListAsync(ct);
        var previousChecks = from check in db.PullRequestScopeChecks
                             join binding in db.TaskPullRequestBindings on check.TaskPullRequestBindingId equals binding.TaskPullRequestBindingId
                             where binding.ProjectRepositoryId == repository.ProjectRepositoryId && binding.PullRequestNumber == number
                             select check.HeadSha;
        var heads = attempts.Concat(await previousChecks.ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        foreach (var head in heads)
            await github.PublishCheckAsync(repository.InstallationId, repository.GitHubRepositoryId,
                repository.Owner, repository.Name, head, false, reason, ct);
        return heads;
    }

    private async Task<string> RecordAttemptAsync(ProjectRepository repository, int number, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Scope validation must register its recovery head outside any enclosing transaction.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(repository.ProjectId, ct);
        var registration = await registry.RequireCurrentAsync(repository, ct);
        const string pending = "Scope verification is in progress; prior results are invalid.";
        var known = await InvalidateKnownHeadsAsync(repository, number, pending, ct);
        var identity = await github.GetPullRequestIdentityAsync(repository.InstallationId, repository.GitHubRepositoryId,
            repository.Owner, repository.Name, number, ct);
        if (!known.Contains(identity.HeadSha))
            await github.PublishCheckAsync(repository.InstallationId, repository.GitHubRepositoryId,
                repository.Owner, repository.Name, identity.HeadSha, false, pending, ct);
        if (!identity.IsOpen) throw new InvalidOperationException("Only an open PR can receive current scope approval.");
        control.PullRequestScopeAttempts.Add(new()
        {
            RegistrationId = registration.RegistrationId,
            OrganizationId = registration.OrganizationId,
            StorageKey = registration.StorageKey,
            ProjectId = repository.ProjectId,
            ProjectRepositoryId = repository.ProjectRepositoryId,
            PullRequestNumber = number,
            HeadSha = identity.HeadSha
        });
        // Recovery heads must survive cancellation or rollback after GitHub has accepted a success.
        await control.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return identity.HeadSha;
    }

    public async Task ProcessWebhookAsync(long repositoryId, long installationId, int number, CancellationToken ct)
    {
        var repositories = await db.ProjectRepositories.Where(item => item.GitHubRepositoryId == repositoryId
            && item.InstallationId == installationId).ToListAsync(ct);
        if (repositories.Count == 0) throw new KeyNotFoundException();
        foreach (var repository in repositories)
        {
            var head = await RecordAttemptAsync(repository, number, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await db.AcquirePlanningPolicyLockAsync(repository.ProjectId, ct);
            await registry.RequireCurrentAsync(repository, ct);
            var binding = await db.TaskPullRequestBindings.SingleOrDefaultAsync(item =>
                item.ProjectRepositoryId == repository.ProjectRepositoryId && item.PullRequestNumber == number, ct);
            if (binding is not null) { await ValidateLockedAsync(binding, repository, head, ct); await transaction.CommitAsync(ct); continue; }
            var exception = await db.NonTaskPullRequestExceptions.SingleOrDefaultAsync(item =>
                item.ProjectRepositoryId == repository.ProjectRepositoryId && item.PullRequestNumber == number, ct);
            var pr = await github.GetPullRequestIdentityAsync(installationId, repositoryId, repository.Owner, repository.Name, number, ct);
            if (pr.HeadSha != head) throw new InvalidOperationException("The PR head changed. Retry scope classification.");
            if (!pr.IsOpen) throw new InvalidOperationException("The PR is no longer open.");
            await github.EnsureExclusivePullRequestHeadAsync(installationId, repositoryId,
                repository.Owner, repository.Name, number, pr.HeadSha, ct);
            await github.PublishCheckAsync(installationId, repositoryId, repository.Owner, repository.Name, pr.HeadSha,
                exception is not null, exception is null
                    ? "Strict scope policy: bind an approved task or obtain an Owner-approved non-task exception."
                    : $"Owner-approved non-task exception: {exception.Reason}", ct);
            if (exception is not null && !await registry.IsCurrentAsync(repository, ct))
            {
                await github.PublishCheckAsync(installationId, repositoryId, repository.Owner, repository.Name, pr.HeadSha,
                    false, "Repository authority changed during classification.", ct);
                throw new InvalidOperationException("Repository authority changed during classification.");
            }
            await transaction.CommitAsync(ct);
        }
    }
}
