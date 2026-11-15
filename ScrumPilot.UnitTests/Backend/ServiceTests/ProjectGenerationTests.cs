using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class ProjectGenerationTests
{
    [Fact]
    public async Task PolicyWrites_AcquireSharedLockBeforeReadingOrInvalidatingInputs()
    {
        var observer = new PolicyLockObserver();
        await using var fixture = await Fixture.Create(observer);
        observer.Commands.Clear();
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Content", null, false), default);
        AssertLockFirst();
        observer.Commands.Clear();
        await new ProjectGuidelinesService(fixture.Db).SaveAsync(fixture.ProjectId, new("Guidance", null), "owner", default);
        AssertLockFirst();
        observer.Commands.Clear();
        await fixture.Service.SaveProblemAsync(fixture.ProjectId, source.ProblemStatementId, new("Source", "Changed", source.Version, false), default);
        AssertLockFirst();
        var legacy = new ProductBacklogItem { ProjectId = fixture.ProjectId, Title = "Legacy", Type = PbiType.Story };
        fixture.Db.Stories.Add(legacy);
        await fixture.Db.SaveChangesAsync();
        observer.Commands.Clear();
        await fixture.Service.LinkLegacyAsync(fixture.ProjectId, legacy.PbiId, new(source.ProblemStatementId, legacy.Version), default);
        AssertLockFirst();
        var currency = await ProjectStoryCurrency.ReadAsync(fixture.Db, fixture.ProjectId, legacy.PbiId);
        observer.Commands.Clear();
        await fixture.Service.ReviewApplicabilityAsync(fixture.ProjectId, legacy.PbiId, new(currency.CurrentFingerprint, "Applicable"), "owner", default);
        AssertLockFirst();

        void AssertLockFirst()
        {
            Assert.NotEmpty(observer.Commands);
            var lockIndex = observer.Commands.FindIndex(command => command.Sql.Contains("sp_getapplock", StringComparison.Ordinal));
            Assert.True(lockIndex >= 0);
            Assert.All(observer.Commands.Take(lockIndex), command => Assert.Contains("TenantMetadata", command.Sql));
            Assert.True(observer.Commands[lockIndex].InTransaction);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerationRejectsSourceOrGuidanceChangedWhileInferenceHasNoLease(bool changeGuidance)
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Content", null, false), default);
        fixture.Handler.DuringInference = async () =>
        {
            Assert.False(fixture.Db.HasStorageLease);
            Assert.Equal(System.Data.ConnectionState.Closed, fixture.Db.Database.GetDbConnection().State);
            await using var writer = fixture.Sql.NewContext();
            if (changeGuidance)
                await new ProjectGuidelinesService(writer).SaveAsync(fixture.ProjectId, new("Changed guidance", null), "owner", default);
            else
            {
                var current = await writer.ProblemStatements.SingleAsync();
                current.Text = "Changed source";
                await writer.SaveChangesAsync();
            }
        };
        var batch = await fixture.Service.GenerateAsync(fixture.ProjectId,
            new("changed-during-inference", [source.ProblemStatementId]), "owner", default);
        Assert.Equal(GenerationState.Failed, Assert.Single(batch.Results).State);
        Assert.Empty(batch.Results[0].Proposals);
        Assert.Empty(await fixture.Db.Stories.ToArrayAsync());
    }

    [Fact]
    public async Task GenerationAndEditedAcceptance_RejectDescriptionsOverTwoThousandCharacters()
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Content", null, false), default);
        fixture.Handler.RawOutput = JsonSerializer.Serialize(new
        { stories = new[] { new { title = "Too long", description = new string('x', 2001), priority = "Low", storyPoints = "Zero" } } });
        var failed = await fixture.Service.GenerateAsync(fixture.ProjectId, new("too-long", [source.ProblemStatementId]), "actor", default);
        Assert.Equal("invalid_story_output", failed.Results[0].ErrorCode);
        Assert.Empty(await fixture.Db.Stories.ToListAsync());
        fixture.Handler.RawOutput = JsonSerializer.Serialize(new
        { stories = new[] { new { title = "Within limit", description = new string('x', 2000), priority = "Low", storyPoints = "Zero" } } });
        var generated = await fixture.Service.GenerateAsync(fixture.ProjectId, new("valid", [source.ProblemStatementId]), "actor", default);
        var run = generated.Results[0];
        var proposal = run.Proposals[0];
        var request = new AcceptProjectStoriesRequest(run.GenerationRunId!.Value, false,
            [new(proposal.ProposalId, 0, "Edited", new string('x', 2001), PbiPriority.Low, PbiPoints.Zero)]);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.AcceptAsync(fixture.ProjectId, request, default));
        Assert.Empty(await fixture.Db.Stories.ToListAsync());
        var saved = await fixture.Service.AcceptAsync(fixture.ProjectId,
            request with { Items = [request.Items[0] with { Description = new string('x', 2000) }] }, default);
        Assert.Equal(2000, saved[0].Description.Length);
    }

    [Fact]
    public async Task ApplicabilityReview_RecoversStaleContentAndExpiresWithoutChangingProvenanceOrDependencyReview()
    {
        await using var fixture = await Fixture.Create();
        var (source, story) = await AcceptedStoryAsync(fixture);
        var originalInput = (await fixture.Db.GenerationInputs.AsNoTracking().SingleAsync()).SnapshotJson;
        var originalRun = story.GenerationRunId;
        await new ProjectGuidelinesService(fixture.Db).SaveAsync(fixture.ProjectId, new("New guidance", null), "owner", default);
        await fixture.Service.SaveProblemAsync(fixture.ProjectId, source.ProblemStatementId,
            new(source.Title, "Revised source", source.Version, true), default);
        var stale = await ProjectStoryCurrency.ReadAsync(fixture.Db, fixture.ProjectId, story.PbiId);
        Assert.True(stale.RequiresReview);
        var reviewed = await fixture.Service.ReviewApplicabilityAsync(fixture.ProjectId, story.PbiId,
            new(stale.CurrentFingerprint, "Existing requirements still apply to this archived source."), "owner", default);
        Assert.True(reviewed.ApplicabilityReviewed);
        Assert.False(reviewed.RequiresReview);
        Assert.False(reviewed.SourceChanged);
        Assert.False(reviewed.GuidelinesChanged);
        Assert.False(reviewed.SourceArchived);
        var audit = await fixture.Db.PlanningArtifactReviews.AsNoTracking().SingleAsync();
        Assert.Equal("owner", audit.ActorId);
        Assert.Equal(stale.CurrentFingerprint, audit.Fingerprint);
        Assert.Equal(story.PbiId, audit.PbiId);
        Assert.Contains("still apply", audit.Reason);
        Assert.Equal(originalRun, (await fixture.Db.Stories.AsNoTracking().SingleAsync()).GenerationRunId);
        Assert.Equal(originalInput, (await fixture.Db.GenerationInputs.AsNoTracking().SingleAsync()).SnapshotJson);

        story.Title = "Changed story requirements";
        await fixture.Db.SaveChangesAsync();
        var changed = await ProjectStoryCurrency.ReadAsync(fixture.Db, fixture.ProjectId, story.PbiId);
        Assert.True(changed.ContentChanged);
        Assert.True(changed.RequiresReview);
        Assert.NotEqual(stale.CurrentFingerprint, changed.CurrentFingerprint);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => fixture.Service.ReviewApplicabilityAsync(
            fixture.ProjectId, story.PbiId, new(stale.CurrentFingerprint, "Old browser tab"), "owner", default));
        Assert.Equal(1, await fixture.Db.PlanningArtifactReviews.CountAsync());

        fixture.Db.DependencyReviews.Add(new() { ProjectId = fixture.ProjectId, PbiId = story.PbiId, InvalidationRevision = 2 });
        await fixture.Db.SaveChangesAsync();
        var acknowledged = await fixture.Service.ReviewApplicabilityAsync(fixture.ProjectId, story.PbiId,
            new(changed.CurrentFingerprint, "Reviewed the revised requirements."), "owner", default);
        Assert.True(acknowledged.ApplicabilityReviewed);
        Assert.False(acknowledged.ContentChanged);
        Assert.True(acknowledged.DependencyReviewRequired);
        Assert.True(acknowledged.RequiresReview);
        Assert.Null((await fixture.Db.DependencyReviews.AsNoTracking().SingleAsync()).ReviewedRevision);
        var latestSource = await fixture.Db.ProblemStatements.AsNoTracking().SingleAsync();
        await fixture.Service.SaveProblemAsync(fixture.ProjectId, source.ProblemStatementId,
            new(latestSource.Title, "Another source change", latestSource.Version, false), default);
        Assert.True((await ProjectStoryCurrency.ReadAsync(fixture.Db, fixture.ProjectId, story.PbiId)).SourceChanged);
    }

    [Fact]
    public async Task ApplicabilityReview_IsOwnerOnlyAndAppendOnly()
    {
        await using var fixture = await Fixture.Create();
        var (_, story) = await AcceptedStoryAsync(fixture);
        var currency = await ProjectStoryCurrency.ReadAsync(fixture.Db, fixture.ProjectId, story.PbiId);
        var access = Substitute.For<IOrganizationAccessService>();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns("actor");
        access.CanAccessProjectAsync("actor", fixture.ProjectId, default).Returns(true);
        access.GetOrganizationIdForProjectAsync(fixture.ProjectId, default).Returns(1);
        var controller = new ProjectStoryGenerationController(fixture.Service, user, access);
        var request = new ReviewStoryApplicabilityRequest(currency.CurrentFingerprint, "Applicable to the current project.");
        Assert.IsType<ForbidResult>(await controller.Review(fixture.ProjectId, story.PbiId, request, default));
        Assert.Empty(await fixture.Db.PlanningArtifactReviews.ToListAsync());
        access.IsOrganizationOwnerAsync("actor", 1, default).Returns(true);
        Assert.IsType<OkObjectResult>(await controller.Review(fixture.ProjectId, story.PbiId, request, default));
        var audit = await fixture.Db.PlanningArtifactReviews.SingleAsync();
        audit.Reason = "Edited audit";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
        fixture.Db.Entry(audit).State = EntityState.Unchanged;
        fixture.Db.PlanningArtifactReviews.Remove(audit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Currency_ReadManyBatchesTwoHundredStoriesWithConstantQueryCount()
    {
        await using var fixture = await Fixture.Create();
        fixture.Handler.RawOutput = JsonSerializer.Serialize(new
        {
            stories = Enumerable.Range(1, 100).Select(i => new
            { title = $"Story {i}", description = "Observable outcome", priority = "Low", storyPoints = "Zero" })
        });
        var first = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("First", "First source", null, false), default);
        var second = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Second", "Second source", null, false), default);
        var batch = await fixture.Service.GenerateAsync(fixture.ProjectId,
            new("batch-currency", [first.ProblemStatementId, second.ProblemStatementId]), "actor", default);
        var accepted = new List<ProductBacklogItem>();
        foreach (var run in batch.Results)
            accepted.AddRange(await fixture.Service.AcceptAsync(fixture.ProjectId, new(run.GenerationRunId!.Value, false,
                run.Proposals.Select(p => new AcceptStoryProposal(p.ProposalId, 0, p.Pbi.Title, p.Pbi.Description,
                    p.Pbi.Priority, p.Pbi.StoryPoints)).ToArray()), default));
        await fixture.Service.SaveProblemAsync(fixture.ProjectId, second.ProblemStatementId,
            new("Edited", "Changed source", second.Version, true), default);
        await new ProjectGuidelinesService(fixture.Db).SaveAsync(fixture.ProjectId, new("New guidance", null), "owner", default);
        fixture.Db.DependencyReviews.Add(new()
        { ProjectId = fixture.ProjectId, PbiId = accepted[0].PbiId, InvalidationRevision = 1 });
        var foreignProject = new Project { OrganizationId = 1, ProjectName = "Other" };
        fixture.Db.Projects.Add(foreignProject);
        await fixture.Db.SaveChangesAsync();
        var foreign = new ProductBacklogItem { ProjectId = foreignProject.ProjectId, Title = "Not accessible" };
        fixture.Db.Stories.Add(foreign);
        await fixture.Db.SaveChangesAsync();

        var counter = new CurrencyQueryCounter();
        await using var reading = fixture.Sql.NewContext(counter);
        var ids = accepted.Select(p => p.PbiId).Concat([foreign.PbiId, int.MaxValue, accepted[0].PbiId]).ToArray();
        var currencies = await ProjectStoryCurrency.ReadManyAsync(reading, fixture.ProjectId, ids);
        Assert.Equal(200, currencies.Count);
        Assert.InRange(counter.Reads, 6, 7);
        Assert.True(currencies[accepted[0].PbiId].DependencyReviewRequired);
        Assert.False(currencies[accepted[1].PbiId].DependencyReviewRequired);
        Assert.All(accepted.Take(100), p => Assert.False(currencies[p.PbiId].SourceChanged));
        Assert.All(accepted.Skip(100), p =>
        {
            Assert.True(currencies[p.PbiId].SourceChanged);
            Assert.True(currencies[p.PbiId].SourceArchived);
        });
        Assert.All(currencies.Values, c => Assert.True(c.GuidelinesChanged));
        Assert.Equal(currencies[accepted[0].PbiId],
            await ProjectStoryCurrency.ReadAsync(reading, fixture.ProjectId, accepted[0].PbiId));
        fixture.Db.PlanningArtifactReviews.Add(new()
        {
            ProjectId = fixture.ProjectId,
            PbiId = accepted[0].PbiId,
            Fingerprint = currencies[accepted[0].PbiId].CurrentFingerprint,
            ActorId = "owner",
            Reason = "Current applicability reviewed."
        });
        await fixture.Db.SaveChangesAsync();
        var beforeReviewed = counter.Reads;
        var withReview = await ProjectStoryCurrency.ReadManyAsync(reading, fixture.ProjectId, ids);
        Assert.InRange(counter.Reads - beforeReviewed, 6, 7);
        Assert.True(withReview[accepted[0].PbiId].ApplicabilityReviewed);
        Assert.False(withReview[accepted[0].PbiId].GuidelinesChanged);
        Assert.True(withReview[accepted[0].PbiId].DependencyReviewRequired);
        Assert.False(withReview[accepted[1].PbiId].ApplicabilityReviewed);
        Assert.True(withReview[accepted[1].PbiId].GuidelinesChanged);
        var beforeEmpty = counter.Reads;
        Assert.Empty(await ProjectStoryCurrency.ReadManyAsync(reading, fixture.ProjectId, []));
        Assert.Equal(beforeEmpty, counter.Reads);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            ProjectStoryCurrency.ReadAsync(reading, fixture.ProjectId, foreign.PbiId));
    }

    [Fact]
    public async Task RequiredCheckInvalidation_AppFailurePreventsGuidelineSourceAndLinkWrites()
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Original source", null, false), default);
        var linked = new ProductBacklogItem { ProjectId = fixture.ProjectId, Title = "Linked", Type = PbiType.Story, ProblemStatementId = source.ProblemStatementId };
        var legacy = new ProductBacklogItem { ProjectId = fixture.ProjectId, Title = "Legacy", Type = PbiType.Story };
        fixture.Db.Stories.AddRange(linked, legacy);
        await fixture.Db.SaveChangesAsync();
        var tasks = new[] { linked, legacy }.Select(story => new ProductBacklogItem
        { ProjectId = fixture.ProjectId, Title = "Child", Type = PbiType.Task, ParentStoryId = story.PbiId }).ToArray();
        fixture.Db.Stories.AddRange(tasks);
        var repository = new ScrumPilot.Shared.Models.ProjectRepository
        {
            ProjectId = fixture.ProjectId,
            GitHubRepositoryId = 1,
            InstallationId = 1,
            Owner = "org",
            Name = "repo",
            DefaultBranch = "main"
        };
        fixture.Db.ProjectRepositories.Add(repository);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.TaskPullRequestBindings.AddRange(tasks.Select((task, index) => new TaskPullRequestBinding
        {
            ProjectId = fixture.ProjectId,
            PbiId = task.PbiId,
            ProjectRepositoryId = repository.ProjectRepositoryId,
            PullRequestNumber = index + 1,
            ActorId = "owner"
        }));
        await fixture.Db.SaveChangesAsync();
        var github = new GitHubAppClient(new HttpClient(fixture.Handler), Options.Create(new GitHubAppOptions()));
        var notifier = new ProjectWorkNotifier(Substitute.For<IHubContext<ProjectWorkHub>>(), new(),
            Substitute.For<IOrganizationAccessService>(), NullLogger<ProjectWorkNotifier>.Instance, fixture.Sql.Selection);
        await fixture.Sql.RegisterAsync(repository);
        var scopes = new PullRequestScopeService(fixture.Db, github,
            new TaskSpecificationService(fixture.Db, github, notifier, fixture.Sql.Registry), fixture.Sql.Control, fixture.Sql.Registry);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProjectGuidelinesService(fixture.Db, scopes).SaveAsync(fixture.ProjectId, new("Changed", null), "owner", default));
        Assert.Contains("GitHub App is not configured", error.Message);
        Assert.Empty(await fixture.Db.ProjectGuidelinesRevisions.ToListAsync());
        Assert.Null(await fixture.Db.Projects.Select(p => p.CurrentGuidelinesRevisionId).SingleAsync());
        var service = new ProjectStoryGenerationService(fixture.Db, fixture.Ai,
            fixture.Planning, fixture.Sql.Inference, fixture.Options, scopes: scopes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveProblemAsync(fixture.ProjectId,
            source.ProblemStatementId, new("Changed", "Changed source", source.Version, false), default));
        Assert.Equal("Original source", (await fixture.Db.ProblemStatements.AsNoTracking().SingleAsync()).Text);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkLegacyAsync(fixture.ProjectId,
            legacy.PbiId, new(source.ProblemStatementId, legacy.Version), default));
        Assert.Null((await fixture.Db.Stories.AsNoTracking().SingleAsync(p => p.PbiId == legacy.PbiId)).ProblemStatementId);
        var currency = await ProjectStoryCurrency.ReadAsync(fixture.Db, fixture.ProjectId, linked.PbiId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewApplicabilityAsync(fixture.ProjectId,
            linked.PbiId, new(currency.CurrentFingerprint, "Applicable"), "owner", default));
        Assert.Empty(await fixture.Db.PlanningArtifactReviews.ToListAsync());
    }

    [Fact]
    public async Task Guidelines_AppendRestoreAndRejectStaleAndOversizeWrites()
    {
        await using var fixture = await Fixture.Create();
        var service = new ProjectGuidelinesService(fixture.Db);
        var first = await service.SaveAsync(fixture.ProjectId, new("one", null), "owner", default);
        var second = await service.SaveAsync(fixture.ProjectId, new("two", first.ProjectGuidelinesRevisionId), "owner", default);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            service.SaveAsync(fixture.ProjectId, new("stale", first.ProjectGuidelinesRevisionId), "owner", default));
        var restored = await service.RestoreAsync(fixture.ProjectId, first.ProjectGuidelinesRevisionId,
            new(second.ProjectGuidelinesRevisionId), "owner", default);
        Assert.Equal(3, restored.Revision);
        Assert.Equal("one", restored.Content);
        Assert.Equal(3, await fixture.Db.ProjectGuidelinesRevisions.CountAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(fixture.ProjectId,
            new(new string('é', 65537), restored.ProjectGuidelinesRevisionId), "owner", default));
        await service.SaveAsync(fixture.ProjectId, new(new string('é', 65536), restored.ProjectGuidelinesRevisionId), "owner", default);
        Assert.Contains("40", ProjectGuidelinesService.Template());
    }

    [Fact]
    public async Task Stories_PersistSnapshotsReplayAndAcceptWithoutDuplicatesOrTrustingIds()
    {
        await using var fixture = await Fixture.Create();
        var guidance = new ProjectGuidelinesService(fixture.Db);
        await guidance.SaveAsync(fixture.ProjectId, new("Keep private", null), "owner", default);
        var service = fixture.Service;
        var source = await service.SaveProblemAsync(fixture.ProjectId, null, new("Need", "A useful feature", null, false), default);
        var request = new GenerateProjectStoriesRequest("batch", [source.ProblemStatementId]);
        var batch = await service.GenerateAsync(fixture.ProjectId, request, "member", default);
        var run = Assert.Single(batch.Results);
        Assert.Null(run.ErrorCode);
        Assert.Equal(2, run.Proposals.Count);
        var replay = await service.GenerateAsync(fixture.ProjectId, request, "member", default);
        Assert.Equal(run.GenerationRunId, replay.Results[0].GenerationRunId);
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.Equal(0, ((DirectSave)fixture.Planning).Calls);
        Assert.Empty(await fixture.Db.Stories.ToListAsync());
        var proposal = run.Proposals[0];
        Assert.All(run.Proposals, p => Assert.False(string.IsNullOrEmpty(p.Pbi.GenerationCandidateKey)));
        Assert.Equal(2, run.Proposals.Select(p => p.Pbi.GenerationCandidateKey).Distinct().Count());
        var edit = new AcceptStoryProposal(proposal.ProposalId, proposal.Pbi.Version, "Edited", "Reviewed", PbiPriority.High, PbiPoints.Zero);
        var accept = new AcceptProjectStoriesRequest(run.GenerationRunId!.Value, false, [edit]);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AcceptAsync(fixture.ProjectId, accept with { Items = [edit, edit with { ProposalId = 99999 }] }, default));
        Assert.Empty(await fixture.Db.Stories.ToListAsync());
        var saved = await service.AcceptAsync(fixture.ProjectId, accept, default);
        Assert.False(Assert.Single(saved).IsDraft);
        await service.AcceptAsync(fixture.ProjectId, accept, default);
        Assert.Equal(1, await fixture.Db.Stories.CountAsync());
        Assert.Equal(source.ProblemStatementId, saved[0].ProblemStatementId);
        var remaining = run.Proposals[1];
        var remainingRequest = new AcceptProjectStoriesRequest(run.GenerationRunId.Value, true,
            [new(remaining.ProposalId, remaining.Pbi.Version, "Second reviewed", remaining.Pbi.Description,
                remaining.Pbi.Priority, remaining.Pbi.StoryPoints)]);
        Assert.True(Assert.Single(await service.AcceptAsync(fixture.ProjectId, remainingRequest, default)).IsDraft);
        await service.AcceptAsync(fixture.ProjectId, remainingRequest, default);
        Assert.Equal(2, await fixture.Db.Stories.CountAsync());
        Assert.False((await fixture.Db.Stories.FindAsync(saved[0].PbiId))!.IsDraft);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.AcceptAsync(fixture.ProjectId, accept with { Items = [edit with { ProposalId = 99999 }] }, default));
        var snapshot = await fixture.Db.GenerationInputs.SingleAsync();
        Assert.Contains("Keep private", snapshot.SnapshotJson);
        Assert.Contains("A useful feature", snapshot.SnapshotJson);
        await service.SaveProblemAsync(fixture.ProjectId, source.ProblemStatementId,
            new("Need edited", "Changed source", source.Version, false), default);
        Assert.True((await service.CurrencyAsync(fixture.ProjectId, saved[0].PbiId, default)).SourceChanged);
        var current = await guidance.CurrentAsync(fixture.ProjectId, default);
        await guidance.SaveAsync(fixture.ProjectId, new("Updated guidance", current!.ProjectGuidelinesRevisionId), "owner", default);
        Assert.True((await service.CurrencyAsync(fixture.ProjectId, saved[0].PbiId, default)).GuidelinesChanged);
        Assert.Equal(await service.CurrencyAsync(fixture.ProjectId, saved[0].PbiId, default),
            await ProjectStoryCurrency.ReadAsync(fixture.Db, fixture.ProjectId, saved[0].PbiId));
        Assert.Equal("Reviewed", saved[0].Description);
        var newRun = await service.GenerateAsync(fixture.ProjectId, request with { IdempotencyKey = "second-run" }, "member", default);
        Assert.NotEqual(run.GenerationRunId, newRun.Results[0].GenerationRunId);
        Assert.Equal(2, await fixture.Db.Stories.CountAsync());
    }

    [Fact]
    public async Task Acceptance_RollsBackAllChosenItemsIfMutationPersistenceFails()
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Content", null, false), default);
        var batch = await fixture.Service.GenerateAsync(fixture.ProjectId, new("atomic", [source.ProblemStatementId]), "actor", default);
        var run = batch.Results[0];
        var request = new AcceptProjectStoriesRequest(run.GenerationRunId!.Value, false,
            run.Proposals.Select(p => new AcceptStoryProposal(p.ProposalId, 0, p.Pbi.Title, p.Pbi.Description, p.Pbi.Priority, p.Pbi.StoryPoints)).ToArray());
        var planning = (DirectSave)fixture.Planning;
        planning.FailAfterSave = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AcceptAsync(fixture.ProjectId, request, default));
        Assert.Equal(0, await fixture.Db.Stories.CountAsync());
        Assert.Equal(1, await fixture.Db.GenerationInputs.CountAsync());
        fixture.Db.ChangeTracker.Clear();
        planning.FailAfterSave = false;
        Assert.Equal(2, (await fixture.Service.AcceptAsync(fixture.ProjectId, request, default)).Count);
        Assert.Equal(2, await fixture.Db.Stories.CountAsync());
    }

    [Fact]
    public async Task RejectedOrRegeneratedPreviewsLeaveOnlyImmutableAuditAndNoPbis()
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Content", null, false), default);
        var first = await fixture.Service.GenerateAsync(fixture.ProjectId, new("reject", [source.ProblemStatementId]), "actor", default);
        var second = await fixture.Service.GenerateAsync(fixture.ProjectId, new("regenerate", [source.ProblemStatementId]), "actor", default);
        Assert.Equal(2, first.Results[0].Proposals.Count);
        Assert.Equal(2, second.Results[0].Proposals.Count);
        Assert.Empty(await fixture.Db.Stories.ToListAsync());
        Assert.Equal(2, await fixture.Db.GenerationRuns.CountAsync());
        var snapshots = await fixture.Db.GenerationInputs.ToListAsync();
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot =>
        {
            Assert.Contains("Content", snapshot.SnapshotJson);
            Assert.Contains("First", snapshot.SnapshotJson);
            Assert.Contains("story-1", snapshot.SnapshotJson);
        });
    }

    [Fact]
    public async Task Generation_RejectsAiOwnedIdsAndDoesNotSaveUnvalidatedOutput()
    {
        await using var fixture = await Fixture.Create();
        fixture.Handler.RawOutput = """{"stories":[{"title":"Injected","description":"No","priority":"Low","storyPoints":"Zero","problemStatementId":999}]}""";
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Content", null, false), default);
        var batch = await fixture.Service.GenerateAsync(fixture.ProjectId, new("malicious", [source.ProblemStatementId]), "actor", default);
        Assert.Equal("invalid_story_output", batch.Results[0].ErrorCode);
        Assert.Empty(await fixture.Db.Stories.ToListAsync());
    }

    [Fact]
    public async Task Generation_RefusesRevokedMembershipBeforeInference()
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Private content", null, false), default);
        fixture.Sql.Control.OrganizationMemberships.RemoveRange(await fixture.Sql.Control.OrganizationMemberships.ToListAsync());
        await fixture.Sql.Control.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.GenerateAsync(fixture.ProjectId,
            new("no-external", [source.ProblemStatementId]), "actor", default));
        Assert.Equal(0, fixture.Handler.Calls);
        Assert.Empty(await fixture.Db.Stories.ToListAsync());
    }

    [Fact]
    public async Task LegacyLink_IsExplicitAndCannotClaimGenerationOrCrossProjects()
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Content", null, false), default);
        var pbi = new ProductBacklogItem { ProjectId = fixture.ProjectId, Title = "Legacy", Type = PbiType.Story };
        fixture.Db.Stories.Add(pbi);
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.LinkLegacyAsync(fixture.ProjectId,
            pbi.PbiId, new(99999, pbi.Version), default));
        var linked = await fixture.Service.LinkLegacyAsync(fixture.ProjectId, pbi.PbiId, new(source.ProblemStatementId, pbi.Version), default);
        Assert.Equal(source.ProblemStatementId, linked.ProblemStatementId);
        Assert.Null(linked.GenerationRunId);
        Assert.Null(linked.GenerationCandidateKey);
        Assert.True((await fixture.Service.CurrencyAsync(fixture.ProjectId, pbi.PbiId, default)).RequiresReview);
    }

    [Fact]
    public async Task Batch_FailsOneSourceWithoutDiscardingOthersAndRejectsCrossProject()
    {
        await using var fixture = await Fixture.Create();
        var good = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Good", "good", null, false), default);
        var bad = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Bad", "fail-source", null, false), default);
        var response = await fixture.Service.GenerateAsync(fixture.ProjectId, new("mixed", [bad.ProblemStatementId, good.ProblemStatementId]), "actor", default);
        Assert.NotNull(response.Results[0].ErrorCode);
        Assert.Equal(2, response.Results[1].Proposals.Count);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.GenerateAsync(
            fixture.ProjectId, new("foreign", [99999]), "actor", default));
    }

    [Fact]
    public async Task Guidelines_MembersCanReadButOnlyOwnersCanWrite()
    {
        await using var fixture = await Fixture.Create();
        var access = Substitute.For<IOrganizationAccessService>();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns("member");
        access.CanAccessProjectAsync("member", fixture.ProjectId, default).Returns(true);
        access.GetOrganizationIdForProjectAsync(fixture.ProjectId, default).Returns(1);
        var controller = new ProjectGuidelinesController(new(fixture.Db), user, access);
        Assert.IsType<OkObjectResult>(await controller.History(fixture.ProjectId, default));
        Assert.IsType<NotFoundResult>(await controller.Save(fixture.ProjectId, new("no", null), default));
        access.IsOrganizationOwnerAsync("member", 1, default).Returns(true);
        Assert.IsType<OkObjectResult>(await controller.Save(fixture.ProjectId, new("owner write", null), default));
        Assert.IsType<NotFoundResult>(await controller.History(99999, default));
    }

    [Fact]
    public async Task Improve_WithSavedGuidelinesNeverUsesExternalFallbackAndRetainsLineage()
    {
        await using var fixture = await Fixture.Create();
        await new ProjectGuidelinesService(fixture.Db).SaveAsync(fixture.ProjectId,
            new("Private improvement guidance", null), "owner", default);
        var pbi = new ProductBacklogItem { ProjectId = fixture.ProjectId, Title = "Original", Type = PbiType.Story };
        fixture.Db.Stories.Add(pbi);
        await fixture.Db.SaveChangesAsync();
        fixture.Handler.Improvement = true;
        var service = new PbiService(Substitute.For<ScrumPilot.API.Services.AI.IStoryGenerationService>(),
            Substitute.For<IPbiRepository>(), fixture.Db, fixture.Sql.Inference, fixture.Ai);
        var id = pbi.PbiId;
        var result = await service.ImprovePbiAsync(pbi);
        Assert.Equal(id, result.PbiId);
        Assert.Equal(fixture.ProjectId, result.ProjectId);
        Assert.Equal("Improved", result.Title);
        Assert.All(fixture.Handler.Uris, u => Assert.True(u.IsLoopback));
        Assert.Contains("Private improvement guidance", fixture.Handler.LastInput);
    }

    private sealed class AiHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Improvement { get; set; }
        public string? RawOutput { get; set; }
        public List<Uri> Uris { get; } = [];
        public string LastInput { get; private set; } = "";
        public Func<Task>? DuringInference { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Uris.Add(request.RequestUri!);
            var input = await request.Content!.ReadAsStringAsync(ct);
            LastInput = input;
            if (DuringInference is not null) await DuringInference();
            if (input.Contains("fail-source"))
                return new(HttpStatusCode.ServiceUnavailable);
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    response = RawOutput ?? (Improvement
                    ? """{"stories":[{"title":"Improved","description":"Improved content","priority":"Medium","storyPoints":"Zero"}]}"""
                    : """{"stories":[{"title":"First","description":"As a user, I can work.","priority":"Medium","storyPoints":"Zero"},{"title":"Second","description":"Acceptance criteria","priority":"Low","storyPoints":"Zero"}]}""")
                })
            };
        }

    }

    private static async Task<(ProblemStatement Source, ProductBacklogItem Story)> AcceptedStoryAsync(Fixture fixture)
    {
        var source = await fixture.Service.SaveProblemAsync(fixture.ProjectId, null, new("Source", "Original source", null, false), default);
        var batch = await fixture.Service.GenerateAsync(fixture.ProjectId, new(Guid.NewGuid().ToString("N"), [source.ProblemStatementId]), "actor", default);
        var run = batch.Results[0];
        var proposal = run.Proposals[0];
        var saved = await fixture.Service.AcceptAsync(fixture.ProjectId, new(run.GenerationRunId!.Value, false,
            [new(proposal.ProposalId, 0, proposal.Pbi.Title, proposal.Pbi.Description, proposal.Pbi.Priority, proposal.Pbi.StoryPoints)]), default);
        return (source, saved[0]);
    }

    private sealed class Fixture(PlanningSqlFixture sql, int projectId) : IAsyncDisposable
    {
        public PlanningSqlFixture Sql => sql;
        public OrganizationContext Db => sql.Db;
        public int ProjectId { get; } = projectId;
        public AiHandler Handler { get; } = new();
        public IPlanningMutationHandler Planning { get; } = new DirectSave(sql.Db);
        public AiOptions Options { get; } = new() { Model = "test" };
        public LocalPlanningAi Ai => new(new PlanningTestChatClient(new HttpClient(Handler)), Options);
        public ProjectStoryGenerationService Service => new(Db, Ai, Planning, sql.Inference, Options);
        public static async Task<Fixture> Create(params IInterceptor[] interceptors)
        {
            var sql = await PlanningSqlFixture.CreateAsync(interceptors);
            var db = sql.Db;
            var project = new Project { OrganizationId = sql.Organization.OrganizationId, ProjectName = "Test" };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            return new(sql, project.ProjectId);
        }
        public async ValueTask DisposeAsync()
        {
            await sql.DisposeAsync();
        }
    }

    private sealed class DirectSave(OrganizationContext db) : IPlanningMutationHandler
    {
        public int Calls { get; private set; }
        public bool FailAfterSave { get; set; }
        public async Task SaveAsync(CancellationToken ct)
        {
            Calls++;
            await db.SaveChangesAsync(ct);
            if (FailAfterSave) throw new InvalidOperationException("Simulated mutation rejection after persistence.");
        }

    }

    private sealed class CurrencyQueryCounter : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(result);
        }

    }

    private sealed class PolicyLockObserver : DbCommandInterceptor
    {
        public List<(string Sql, bool InTransaction)> Commands { get; } = [];
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Transaction is not null));
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.CommandText, command.Transaction is not null));
            return ValueTask.FromResult(result);
        }
    }
}
