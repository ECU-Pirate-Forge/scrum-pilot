using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ScrumPilot.Data.Context;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using ScrumPilot.API.Configuration;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class PullRequestScopeServiceTests
{
    private static PullRequestScopeService Service(TaskPlanningFixture fixture) =>
        new(fixture.Db, fixture.GitHub, fixture.Specs, fixture.Control, fixture.Registry);

    [Fact]
    public async Task DisabledIntegrationLetsBoundWorkChangeButFencesItsRepositoryWithoutGitHubCalls()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        await Service(f).BindAsync(f.ProjectId, f.TaskId, 7, "owner", default);
        var requestCount = f.Http.Requests.Count;
        var attempts = await f.Control.PullRequestScopeAttempts.CountAsync();
        using var http = new HttpClient(f.Http, disposeHandler: false);
        var features = Options.Create(new FeatureOptions());
        var github = new GitHubAppClient(http, Options.Create(new GitHubAppOptions()), features);
        var registry = new GitHubRepositoryRegistry(f.Control, f.Selection, features);
        var scope = new PullRequestScopeService(f.Db, github, f.Specs, f.Control, registry);
        var dependencies = new ProjectDependencyService(f.Db, f.Notifier, registry, scope: scope);

        f.TaskItem.Description = "Updated without source control.";
        await dependencies.SaveAsync(default);

        Assert.Equal(requestCount, f.Http.Requests.Count);
        var registration = await f.Control.GitHubRepositoryRegistrations.SingleAsync();
        Assert.Equal(ScrumPilot.Data.Models.RepositoryRegistrationState.Suspended, registration.State);
        Assert.Contains("disabled", registration.RecoveryError);
        Assert.False(await f.Registry.IsCurrentAsync(f.Repository, default));
        Assert.Equal(attempts, await f.Control.PullRequestScopeAttempts.CountAsync());
        Assert.Single(await f.Db.TaskPullRequestBindings.ToListAsync());
        await using var persisted = f.NewContext();
        Assert.Equal("Updated without source control.",
            (await persisted.Stories.SingleAsync(item => item.PbiId == f.TaskId)).Description);
        var graph = await dependencies.ReadAsync(f.ProjectId, null, default);
        Assert.Equal("Ready", graph.Nodes.Single(node => node.Item.PbiId == f.TaskId).State);
    }

    [Fact]
    public async Task BindAndRevalidate_AllowedDiffPublishesSuccessAndAppendsChecksForSameBinding()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        var service = Service(f);
        var first = await service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default);
        var binding = await f.Db.TaskPullRequestBindings.SingleAsync();
        var second = await service.ValidateAsync(binding, default);

        Assert.Equal(ScopeCheckState.Passed, first.State);
        Assert.Equal(ScopeCheckState.Passed, second.State);
        Assert.Equal(first.TaskPullRequestBindingId, second.TaskPullRequestBindingId);
        Assert.NotEqual(first.PullRequestScopeCheckId, second.PullRequestScopeCheckId);
        Assert.Equal(TaskPlanningFixture.HeadSha, second.HeadSha);
        Assert.Equal(TaskPlanningFixture.SnapshotSha, second.MergeBaseSha);
        Assert.Equal(1004L, second.GitHubCheckId);
        using var result = JsonDocument.Parse(second.ResultJson);
        Assert.Empty(result.RootElement.GetProperty("Errors").EnumerateArray());
        var approved = await f.Db.TaskSpecificationRevisions.SingleAsync(x => x.Approval == SpecificationApproval.Approved);
        Assert.Equal(approved.TaskSpecificationRevisionId, second.TaskSpecificationRevisionId);
        Assert.Equal(approved.ContentHash, result.RootElement.GetProperty("SpecificationHash").GetString());
        Assert.Equal(["failure", "success", "failure", "success"],
            f.Http.Checks.Select(check => check.GetProperty("conclusion").GetString()!).ToArray());
        Assert.All(f.Http.Checks, check => Assert.Equal(TaskPlanningFixture.HeadSha, check.GetProperty("head_sha").GetString()));
        Assert.Equal(2, await f.Db.PullRequestScopeChecks.CountAsync());
    }

    [Theory]
    [InlineData("tests/UnrelatedTests.cs", "modified", null, "modify")]
    [InlineData("docs/unrelated.md", "modified", null, "modify")]
    [InlineData("src/Renamed.cs", "renamed", "src/Worker.cs", "rename")]
    [InlineData("src/Worker.cs", "removed", null, "delete")]
    public async Task Bind_ForbiddenFileOrOperationPersistsFailedCheck(string path, string status, string? previous, string operation)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        f.Http.Files = [.. f.Http.Files.Where(x => x.Path != path), new(path, status, previous)];
        var service = Service(f);

        var check = await service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default);

        Assert.Equal(ScopeCheckState.Failed, check.State);
        using var result = JsonDocument.Parse(check.ResultJson);
        Assert.Contains(result.RootElement.GetProperty("Errors").EnumerateArray(),
            error => error.GetString()!.Contains($"{operation}: {path}", StringComparison.Ordinal));
        Assert.Equal(["failure", "failure"], f.Http.Checks.Select(x => x.GetProperty("conclusion").GetString()!).ToArray());
        Assert.Single(await f.Db.TaskPullRequestBindings.ToArrayAsync());
        Assert.Single(await f.Db.PullRequestScopeChecks.ToArrayAsync());
    }

    [Fact]
    public async Task Revalidate_ChangedMergeBaseCannotReusePassingCheck()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        var service = Service(f);
        Assert.Equal(ScopeCheckState.Passed, (await service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default)).State);
        f.Http.MergeBaseSha = TaskPlanningFixture.OtherSha;

        var failed = await service.ValidateAsync(await f.Db.TaskPullRequestBindings.SingleAsync(), default);

        Assert.Equal(ScopeCheckState.Failed, failed.State);
        Assert.Equal(TaskPlanningFixture.OtherSha, failed.MergeBaseSha);
        Assert.Contains("merge-base differs", failed.ResultJson);
        Assert.Equal("failure", f.Http.Checks.Last().GetProperty("conclusion").GetString());
    }

    [Theory]
    [InlineData("source")]
    [InlineData("guidance")]
    public async Task Revalidate_StaleSpecificationFailsEvenWhenDiffIsAllowed(string changed)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        var service = Service(f);
        await service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default);
        await f.ChangeInputAsync(changed);

        var check = await service.ValidateAsync(await f.Db.TaskPullRequestBindings.SingleAsync(), default);
        Assert.Equal(ScopeCheckState.Failed, check.State);
        Assert.Equal("failure", f.Http.Checks.Last().GetProperty("conclusion").GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default));
    }

    [Fact]
    public async Task Bind_DraftSpecificationIsRejectedBeforeGitHubOrBindingWrites()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.SaveDraftAsync();
        var service = Service(f);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default));
        Assert.Empty(f.Http.Requests);
        Assert.Empty(await f.Db.TaskPullRequestBindings.ToArrayAsync());
    }

    [Fact]
    public async Task StrictWebhook_UnboundPrFailsUntilExplicitOwnerExceptionAndThenCannotBindTask()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var service = Service(f);
        await service.ProcessWebhookAsync(99, 42, 7, default);
        Assert.Equal(["failure", "failure"], f.Http.Checks.Select(x => x.GetProperty("conclusion").GetString()!).ToArray());
        Assert.Contains("Strict scope policy", f.Http.Checks[1].GetProperty("output").GetProperty("summary").GetString());
        f.Db.NonTaskPullRequestExceptions.Add(new()
        {
            ProjectId = f.ProjectId,
            ProjectRepositoryId = f.Repository.ProjectRepositoryId,
            PullRequestNumber = 7,
            ActorId = "owner",
            Reason = "Independent infrastructure maintenance."
        });
        await f.Db.SaveChangesAsync();

        await service.ProcessWebhookAsync(99, 42, 7, default);
        Assert.Equal(["failure", "failure", "failure", "success"],
            f.Http.Checks.Select(x => x.GetProperty("conclusion").GetString()!).ToArray());
        Assert.Equal("success", f.Http.Checks.Last().GetProperty("conclusion").GetString());
        await f.ApproveAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default));
        Assert.Contains("non-task exception", error.Message);
        Assert.Empty(await f.Db.TaskPullRequestBindings.ToArrayAsync());
    }

    [Fact]
    public async Task Revalidate_SameHeadRetargetedToOversizedDiffInvalidatesPreviousSuccessBeforeThrowing()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        var service = Service(f);
        await service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default);
        Assert.Equal("success", f.Http.Checks.Last().GetProperty("conclusion").GetString());
        var requestOffset = f.Http.Requests.Count;
        f.Http.ChangedFilesCount = 3000;
        f.Http.BaseSha = TaskPlanningFixture.OtherSha;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateAsync(f.Db.TaskPullRequestBindings.Single(), default));

        Assert.Contains("file API limit", error.Message);
        Assert.Equal(["failure", "success", "failure"],
            f.Http.Checks.Select(x => x.GetProperty("conclusion").GetString()!).ToArray());
        Assert.Equal(TaskPlanningFixture.HeadSha, f.Http.Checks.Last().GetProperty("head_sha").GetString());
        var requests = f.Http.Requests.Skip(requestOffset).ToArray();
        var failurePost = Array.IndexOf(requests, "/repos/owner/repository/check-runs");
        var metadataReads = requests.Select((path, index) => (path, index))
            .Where(x => x.path == "/repos/owner/repository/pulls/7").ToArray();
        Assert.Equal(3, metadataReads.Length);
        Assert.InRange(failurePost, 0, metadataReads[0].index - 1);
        Assert.DoesNotContain("/repos/owner/repository/pulls/7/files", requests);
        Assert.Single(await f.Db.PullRequestScopeChecks.ToArrayAsync());
    }

    [Fact]
    public async Task Revalidate_MetadataUnavailableInvalidatesLastKnownApprovedHeadBeforeLookupFails()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        var service = Service(f);
        await service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default);
        var binding = await f.Db.TaskPullRequestBindings.SingleAsync();
        Assert.Equal("success", f.Http.Checks.Last().GetProperty("conclusion").GetString());
        var requestOffset = f.Http.Requests.Count;
        f.Http.MetadataStatus = System.Net.HttpStatusCode.ServiceUnavailable;

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.ValidateAsync(binding, default));

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(["failure", "success", "failure"],
            f.Http.Checks.Select(x => x.GetProperty("conclusion").GetString()!).ToArray());
        Assert.Equal(TaskPlanningFixture.HeadSha, f.Http.Checks.Last().GetProperty("head_sha").GetString());
        var requests = f.Http.Requests.Skip(requestOffset).ToArray();
        var failurePost = Array.IndexOf(requests, "/repos/owner/repository/check-runs");
        var metadataRead = Array.IndexOf(requests, "/repos/owner/repository/pulls/7");
        Assert.InRange(failurePost, 0, metadataRead - 1);
        Assert.DoesNotContain("/repos/owner/repository/pulls/7/files", requests);
        Assert.Single(await f.Db.PullRequestScopeChecks.ToArrayAsync());
        Assert.Single(await f.Db.TaskPullRequestBindings.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstValidation_CancelledAfterPublishingSuccess_DurableJournalAllowsFailureRecoveryWithoutCheckRow(bool nonTask)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        var service = Service(f);
        if (nonTask)
        {
            f.Db.NonTaskPullRequestExceptions.Add(new()
            {
                ProjectId = f.ProjectId,
                ProjectRepositoryId = f.Repository.ProjectRepositoryId,
                PullRequestNumber = 7,
                ActorId = "owner",
                Reason = "Independent maintenance."
            });
            await f.Db.SaveChangesAsync();
        }
        using var cancellation = new CancellationTokenSource();
        f.Http.BeforeCheckResponse = (check, _) =>
        {
            if (check.GetProperty("conclusion").GetString() == "success")
                cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            nonTask ? service.ProcessWebhookAsync(99, 42, 7, cancellation.Token)
                : service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", cancellation.Token));

        f.Http.BeforeCheckResponse = null;
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(["failure", "success"],
            f.Http.Checks.Select(x => x.GetProperty("conclusion").GetString()!).ToArray());
        f.Db.ChangeTracker.Clear();
        // A separate connection must observe durable recovery state even though the
        // transaction that would have recorded the green scope check was cancelled.
        await using var recovery = f.NewContext();
        await using var recoveryControl = f.NewControl();
        var recoveryRegistry = new GitHubRepositoryRegistry(recoveryControl, f.Selection,
            Options.Create(new FeatureOptions { GitHubIntegration = true }));
        var binding = await recovery.TaskPullRequestBindings.SingleOrDefaultAsync();
        if (nonTask) Assert.Null(binding);
        else Assert.Equal(f.TaskId, Assert.IsType<TaskPullRequestBinding>(binding).PbiId);
        Assert.Empty(await recovery.PullRequestScopeChecks.ToArrayAsync());
        var attempt = await recoveryControl.PullRequestScopeAttempts.SingleAsync();
        Assert.Equal(f.ProjectId, attempt.ProjectId);
        Assert.Equal(f.Repository.ProjectRepositoryId, attempt.ProjectRepositoryId);
        Assert.Equal(7, attempt.PullRequestNumber);
        Assert.Equal(TaskPlanningFixture.HeadSha, attempt.HeadSha);

        f.Http.MetadataStatus = System.Net.HttpStatusCode.ServiceUnavailable;
        var requestOffset = f.Http.Requests.Count;
        var recoverySpecs = new TaskSpecificationService(recovery, f.GitHub, f.Notifier, recoveryRegistry);
        var recoveryService = new PullRequestScopeService(recovery, f.GitHub, recoverySpecs, recoveryControl, recoveryRegistry);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            nonTask ? recoveryService.ProcessWebhookAsync(99, 42, 7, default)
                : recoveryService.ValidateAsync(binding!, default));

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(["failure", "success", "failure"],
            f.Http.Checks.Select(x => x.GetProperty("conclusion").GetString()!).ToArray());
        Assert.Equal(TaskPlanningFixture.HeadSha, f.Http.Checks.Last().GetProperty("head_sha").GetString());
        var requests = f.Http.Requests.Skip(requestOffset).ToArray();
        var failurePost = Array.IndexOf(requests, "/repos/owner/repository/check-runs");
        var metadataRead = Array.IndexOf(requests, "/repos/owner/repository/pulls/7");
        Assert.InRange(failurePost, 0, metadataRead - 1);
        Assert.Empty(await recovery.PullRequestScopeChecks.ToArrayAsync());
        Assert.Equal(nonTask ? 0 : 1, await recovery.TaskPullRequestBindings.CountAsync());
    }

    [Fact]
    public async Task Revalidate_TwoOpenPullRequestsSharingPreviouslyApprovedHeadCannotPublishGreen()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        var service = Service(f);
        await service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default);
        f.Http.OpenPullRequestNumbers = [7, 8];
        var requestOffset = f.Http.Requests.Count;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateAsync(f.Db.TaskPullRequestBindings.Single(), default));

        Assert.Contains("commit-scoped", error.Message);
        Assert.Equal(["failure", "success", "failure"],
            f.Http.Checks.Select(x => x.GetProperty("conclusion").GetString()!).ToArray());
        Assert.Equal(TaskPlanningFixture.HeadSha, f.Http.Checks.Last().GetProperty("head_sha").GetString());
        Assert.DoesNotContain("/repos/owner/repository/pulls/7/files", f.Http.Requests.Skip(requestOffset));
        Assert.Single(await f.Db.PullRequestScopeChecks.ToArrayAsync());
    }

    [Fact]
    public async Task Validate_PolicyLockBlocksConcurrentSourceWriter_ThenWriterRetryInvalidatesGreen()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.ApproveAsync();
        var service = Service(f);
        await service.BindAsync(f.ProjectId, f.TaskId, 7, "owner", default);
        var binding = await f.Db.TaskPullRequestBindings.SingleAsync();
        await using var writer = f.NewContext();
        var story = await writer.Stories.SingleAsync(x => x.PbiId == f.Story.PbiId);
        story.Description = "New requirements from the concurrent writer.";
        var writerSpecs = new TaskSpecificationService(writer, f.GitHub, f.Notifier, f.Registry);
        var writerScope = new PullRequestScopeService(writer, f.GitHub, writerSpecs, f.Control, f.Registry);
        var writerService = new ProjectDependencyService(writer, f.Notifier, f.Registry, scope: writerScope);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Http.BeforeCheckResponse = async (check, ct) =>
        {
            if (check.GetProperty("output").GetProperty("summary").GetString()!
                .StartsWith("Scope verification is in progress", StringComparison.Ordinal))
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        };
        var validation = service.ValidateAsync(binding, default);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var blockedWriter = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writerService.SaveAsync(blockedWriter.Token));
            Assert.Equal("failure", f.Http.Checks.Last().GetProperty("conclusion").GetString());
            Assert.False(validation.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await validation.WaitAsync(TimeSpan.FromSeconds(10));
            f.Http.BeforeCheckResponse = null;
        }
        Assert.Equal(ScopeCheckState.Passed, (await validation).State);
        // The rejected writer did not commit. On retry after validation releases the
        // lock, the actual policy writer must invalidate that green before saving.
        await writerService.SaveAsync(default);
        Assert.Equal("failure", f.Http.Checks.Last().GetProperty("conclusion").GetString());
        Assert.False((await f.Specs.StatusAsync(f.ProjectId, f.TaskId, default)).IsCurrent);
        await using var observer = f.NewContext();
        Assert.Equal(story.Description, (await observer.Stories.SingleAsync(x => x.PbiId == story.PbiId)).Description);
    }
}
