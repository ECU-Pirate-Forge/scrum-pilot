using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class ProjectDependencyServiceTests : IAsyncLifetime
{
    private PlanningSqlFixture _sql = default!;
    private OrganizationContext _db = default!;
    private ProjectDependencyService _service = default!;
    private PbiRepository _repository = default!;
    private int _projectId;
    private readonly List<long> _notifications = [];

    public async ValueTask InitializeAsync()
    {
        _sql = await PlanningSqlFixture.CreateAsync();
        _db = _sql.Db;
        var organization = _sql.Organization;
        var project = new Project { ProjectName = "Project", OrganizationId = organization.OrganizationId };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        _projectId = project.ProjectId;
        var hub = Substitute.For<IHubContext<ProjectWorkHub>>();
        var client = Substitute.For<ISingleClientProxy>();
        hub.Clients.Client("connection").Returns(client);
        client.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask).AndDoes(call => _notifications.Add((long)call.ArgAt<object?[]>(1)[1]!));
        var subscriptions = new ProjectWorkSubscriptions();
        subscriptions.Connections["connection"] = new("member", _projectId, organization.OrganizationId);
        var access = Substitute.For<IOrganizationAccessService>();
        access.CanAccessProjectAsync("member", _projectId, Arg.Any<CancellationToken>()).Returns(true);
        var notifier = new ProjectWorkNotifier(hub, subscriptions, access, NullLogger<ProjectWorkNotifier>.Instance, _sql.Selection);
        _service = new(_db, notifier, _sql.Registry);
        _repository = new(_db, _service);
    }

    public async ValueTask DisposeAsync() { await _sql.DisposeAsync(); }

    [Fact]
    public async Task LegacyWritesPreserveMultipleDependencies_AndRejectAmbiguousReplacement()
    {
        var first = await Add("First");
        var second = await Add("Second");
        var dependent = await Add("Dependent");
        var graph = await _service.ReadAsync(_projectId, null, default);
        await _service.SetAsync(_projectId, new(dependent.PbiId, [first.PbiId, second.PbiId], graph.Revision), default);
        dependent.Title = "Unrelated edit";
        await _repository.UpdateAsync(dependent);
        Assert.Equal(2, await _db.PbiDependencies.CountAsync());
        dependent.DependsOnPbiId = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.UpdateAsync(dependent));
    }

    [Fact]
    public async Task StoryCannotCloseWithUnfinishedChild_AndChildInheritsSprint()
    {
        var story = await Add("Story", PbiType.Story);
        var child = new ProductBacklogItem { ProjectId = _projectId, Title = "Child", Type = PbiType.Task, ParentStoryId = story.PbiId };
        await _repository.AddAsync(child);
        Assert.Equal(story.SprintId, child.SprintId);
        story.Status = PbiStatus.Done;
        _notifications.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.UpdateAsync(story));
        Assert.Empty(_notifications);
        _db.ChangeTracker.Clear();
        Assert.Equal(PbiStatus.ToDo, (await _db.Stories.SingleAsync(item => item.PbiId == story.PbiId)).Status);
    }

    [Fact]
    public async Task ReopeningPreservesDoneDependentButInvalidatesDownstreamCompletion()
    {
        var first = await Add("First");
        var dependent = await Add("Dependent");
        var last = await Add("Last");
        var graph = await _service.ReadAsync(_projectId, null, default);
        await _service.SetAsync(_projectId, new(dependent.PbiId, [first.PbiId], graph.Revision), default);
        graph = await _service.ReadAsync(_projectId, null, default);
        await _service.SetAsync(_projectId, new(last.PbiId, [dependent.PbiId], graph.Revision), default);
        first.Status = PbiStatus.Done;
        await _repository.UpdateAsync(first);
        dependent.Status = PbiStatus.Done;
        await _repository.UpdateAsync(dependent);
        first.Status = PbiStatus.ToDo;
        await _repository.UpdateAsync(first);
        graph = await _service.ReadAsync(_projectId, null, default);
        Assert.Equal("Needs review", graph.Nodes.Single(node => node.Item.PbiId == dependent.PbiId).State);
        Assert.Equal(PbiStatus.Done, dependent.Status);
        Assert.False(graph.Nodes.Single(node => node.Item.PbiId == last.PbiId).DependencyReady);
        var review = await _db.DependencyReviews.SingleAsync(item => item.PbiId == dependent.PbiId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ReviewAsync(_projectId, new(dependent.PbiId, review.InvalidationRevision), "reviewer", default));
    }

    [Fact]
    public async Task DraftChildMayBeReviewedAfterParentDone_ButCannotBeCommittedUntilReopened()
    {
        var story = await Add("Story", PbiType.Story);
        story.Status = PbiStatus.Done;
        await _repository.UpdateAsync(story);
        var child = await _repository.AddAsync(new()
        {
            ProjectId = _projectId,
            Title = "Draft child",
            ParentStoryId = story.PbiId,
            Type = PbiType.Task,
            IsDraft = true
        });
        child.IsDraft = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.UpdateAsync(child));
        _db.ChangeTracker.Clear();
        Assert.True((await _db.Stories.SingleAsync(item => item.PbiId == child.PbiId)).IsDraft);
    }

    [Fact]
    public async Task MovingStoryCarriesChildren_AndRejectsAnIndependentChildMove()
    {
        var first = new Sprint { ProjectId = _projectId, SprintTitle = "First" };
        var second = new Sprint { ProjectId = _projectId, SprintTitle = "Second" };
        _db.AddRange(first, second);
        await _db.SaveChangesAsync();
        var story = await Add("Story", PbiType.Story);
        story.SprintId = first.SprintId;
        await _repository.UpdateAsync(story);
        var child = await _repository.AddAsync(new()
        {
            ProjectId = _projectId,
            Title = "Child",
            ParentStoryId = story.PbiId,
            Type = PbiType.Task
        });
        Assert.Equal(first.SprintId, child.SprintId);
        story.SprintId = second.SprintId;
        await _repository.UpdateAsync(story);
        Assert.Equal(second.SprintId, child.SprintId);
        child.SprintId = first.SprintId;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.UpdateAsync(child));
    }

    [Fact]
    public async Task NewProjectFirstMutationChangesGraphRevision()
    {
        Assert.Equal(0, (await _service.ReadAsync(_projectId, null, default)).Revision);
        await Add("First");
        Assert.True((await _service.ReadAsync(_projectId, null, default)).Revision > 0);
    }

    private async Task<ProductBacklogItem> Add(string title, PbiType type = PbiType.Task) =>
        await _repository.AddAsync(new() { ProjectId = _projectId, Title = title, Type = type });
}
