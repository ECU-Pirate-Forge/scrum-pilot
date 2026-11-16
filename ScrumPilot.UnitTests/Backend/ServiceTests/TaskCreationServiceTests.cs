using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class TaskCreationServiceTests
{
    [Fact]
    public async Task PreviewAndAccept_WorkWithoutRepositoryAndIncludeGuidelinesAndHandoff()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        f.Db.RepositorySnapshots.Remove(f.Snapshot);
        await f.Db.SaveChangesAsync();
        f.Db.ProjectRepositories.Remove(f.Repository);
        await f.Db.SaveChangesAsync();

        var handler = new TaskCreationAiHandler();
        handler.SetOutput(new TaskCreationAiTask("first", f.Story.PbiId,
            "Implement worker", "Add the requested worker behavior.", [], []));
        using var http = new HttpClient(handler);
        var service = Service(f, http);

        var preview = await service.PreviewAsync(f.ProjectId,
            new([f.Story.PbiId], "without-repository"), "owner", default);
        var created = await service.AcceptAsync(f.ProjectId,
            new(preview.GenerationRunId, preview.Tasks), "owner", default);
        var task = Assert.Single(created);

        Assert.Equal(f.Story.PbiId, task.ParentStoryId);
        Assert.Equal(PbiType.Task, task.Type);
        Assert.True(task.IsDraft);
        Assert.Contains("Use focused tests.", task.Description);
        Assert.Contains("Review the existing system", task.Description);
        Assert.Contains("exactly five clarifying questions", task.Description);
        Assert.Contains("only the specific files necessary", task.Description);
        Assert.Empty(await f.Db.RepositorySnapshots.ToArrayAsync());
        Assert.Empty(await f.Db.TaskSpecificationRevisions.Where(item => item.PbiId == task.PbiId).ToArrayAsync());
    }

    [Fact]
    public async Task RegenerationReplacesGeneratedTasksPreservesManualTasksAndDeletionCleansHistory()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var handler = new TaskCreationAiHandler();
        handler.SetOutput(
            new TaskCreationAiTask("first", f.Story.PbiId, "First generated task", "Implement first behavior.", [], []),
            new TaskCreationAiTask("second", f.Story.PbiId, "Second generated task", "Implement follow-up behavior.", ["first"], []));
        using var http = new HttpClient(handler);
        var service = Service(f, http);

        var initialPreview = await service.PreviewAsync(f.ProjectId,
            new([f.Story.PbiId], "initial-task-set"), "owner", default);
        var initialTasks = await service.AcceptAsync(f.ProjectId,
            new(initialPreview.GenerationRunId, initialPreview.Tasks), "owner", default);
        var firstTask = initialTasks[0];
        await f.Specs.SaveAsync(f.ProjectId, firstTask.PbiId,
            new(TaskPlanningFixture.Content(), f.Manifest, f.Snapshot.RepositorySnapshotId, 0), "owner", default);
        var manualTask = await service.CreateManualAsync(f.ProjectId,
            new(f.Story.PbiId, "Manual task", "Preserve this independently entered task."), default);

        handler.SetOutput(new TaskCreationAiTask("replacement", f.Story.PbiId,
            "Replacement task", "Implement revised behavior.", [], []));
        var replacementPreview = await service.PreviewAsync(f.ProjectId,
            new([f.Story.PbiId], "replacement-task-set", ReplaceGeneratedTasks: true), "owner", default);
        var replacement = await service.AcceptAsync(f.ProjectId,
            new(replacementPreview.GenerationRunId, replacementPreview.Tasks), "owner", default);

        var activeChildren = await f.Db.Stories.Where(item => item.ProjectId == f.ProjectId
            && item.ParentStoryId == f.Story.PbiId && item.Type == PbiType.Task).ToArrayAsync();
        Assert.Equal(2, activeChildren.Length);
        Assert.Contains(activeChildren, item => item.PbiId == manualTask.PbiId);
        Assert.Contains(activeChildren, item => item.PbiId == Assert.Single(replacement).PbiId);
        Assert.DoesNotContain(activeChildren, item => initialTasks.Any(old => old.PbiId == item.PbiId));
        Assert.Empty(await f.Db.TaskSpecificationRevisions.Where(item => item.PbiId == firstTask.PbiId).ToArrayAsync());
        Assert.Empty(await f.Db.PbiDependencies.Where(item =>
            initialTasks.Select(task => task.PbiId).Contains(item.DependentPbiId)
            || initialTasks.Select(task => task.PbiId).Contains(item.PrerequisitePbiId)).ToArrayAsync());
        Assert.Equal(GenerationState.Cancelled,
            (await f.Db.GenerationRuns.SingleAsync(item => item.GenerationRunId == initialPreview.GenerationRunId)).State);

        await service.DeleteAsync(f.ProjectId, Assert.Single(replacement).PbiId, default);
        Assert.False(await f.Db.Stories.AnyAsync(item => item.PbiId == replacement[0].PbiId));
        Assert.True(await f.Db.Stories.AnyAsync(item => item.PbiId == manualTask.PbiId));

        var bug = new ProductBacklogItem
        {
            ProjectId = f.ProjectId,
            Title = "A bug",
            Description = "A reproducible issue.",
            Type = PbiType.Bug,
            Status = PbiStatus.ToDo
        };
        f.Db.Stories.Add(bug);
        await f.Db.SaveChangesAsync();
        var bugTask = await service.CreateManualAsync(f.ProjectId,
            new(bug.PbiId, "Fix bug", "Implement the repair."), default);
        Assert.Equal(bug.PbiId, bugTask.ParentStoryId);
        Assert.True(bugTask.IsDraft);
        Assert.Equal(PbiStatus.ToDo, bugTask.Status);

        bug.Status = PbiStatus.Done;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateManualAsync(f.ProjectId, new(bug.PbiId, "Another task", "Should be rejected."), default));
    }

    private static TaskCreationService Service(TaskPlanningFixture f, HttpClient http)
    {
        var options = new AiOptions { Model = "fake-task-model" };
        return new(f.Db, new LocalPlanningAi(new PlanningTestChatClient(http), options),
            new ProjectDependencyService(f.Db, f.Notifier, f.Registry), f.Notifier, options,
            NullLogger<TaskCreationService>.Instance, f.Inference);
    }

    private sealed class TaskCreationAiHandler : HttpMessageHandler
    {
        private string _output = "";
        public void SetOutput(params TaskCreationAiTask[] tasks) =>
            _output = JsonSerializer.Serialize(new TaskCreationAiOutput(tasks));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("http://127.0.0.1:11434/api/generate", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = _output })
            });
        }
    }
}
