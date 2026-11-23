using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaSharp;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class TaskCreationServiceTests
{
    [Theory]
    [InlineData(PbiType.Story)]
    [InlineData(PbiType.Bug)]
    public async Task PreviewSendsOnlySelectedSourceAndItsExistingTasks(PbiType sourceType)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        f.Story.Type = sourceType;
        f.TaskItem.Origin = PbiOrigin.AiGenerated;
        var otherStory = new ProductBacklogItem { ProjectId = f.ProjectId, Title = "Unselected story", Type = PbiType.Story };
        f.Db.Stories.Add(otherStory);
        await f.Db.SaveChangesAsync();
        var manualTask = new ProductBacklogItem
        {
            ProjectId = f.ProjectId, Title = "Manual child task", Type = PbiType.Task, ParentStoryId = f.Story.PbiId
        };
        var otherTask = new ProductBacklogItem
        {
            ProjectId = f.ProjectId, Title = "Unrelated child task", Type = PbiType.Task, ParentStoryId = otherStory.PbiId
        };
        f.Db.Stories.AddRange(manualTask, otherTask,
            new ProductBacklogItem { ProjectId = f.ProjectId, Title = "Standalone task", Type = PbiType.Task });
        await f.Db.SaveChangesAsync();
        f.Db.PbiDependencies.AddRange(
            new() { ProjectId = f.ProjectId, DependentPbiId = f.TaskId, PrerequisitePbiId = manualTask.PbiId },
            new() { ProjectId = f.ProjectId, DependentPbiId = manualTask.PbiId, PrerequisitePbiId = otherTask.PbiId });
        await f.Db.SaveChangesAsync();

        using var handler = new SchemaAwareOllamaHandler(f.Story.PbiId, returnFences: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var chat = new OllamaApiClient(http);
        var preview = await Service(f, chat).PreviewAsync(f.ProjectId,
            new([f.Story.PbiId], "scoped-task-creation", ReplaceGeneratedTasks: true), "owner", default);

        Assert.Equal(f.Story.PbiId, Assert.Single(preview.Tasks).ParentPbiId);
        var userMessage = Assert.Single(handler.Request.GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("role").GetString() == "user");
        using var payload = JsonDocument.Parse(userMessage.GetProperty("content").GetString()!);
        var context = payload.RootElement;
        Assert.Equal(f.Story.PbiId, Assert.Single(context.GetProperty("sources").EnumerateArray()).GetProperty("pbiId").GetInt32());
        Assert.Equal(new[] { f.TaskId, manualTask.PbiId }.Order(),
            context.GetProperty("existingTasks").EnumerateArray().Select(task => task.GetProperty("pbiId").GetInt32()).Order());
        var dependency = Assert.Single(context.GetProperty("dependencies").EnumerateArray());
        Assert.Equal(f.TaskId, dependency.GetProperty("dependentPbiId").GetInt32());
        Assert.Equal(manualTask.PbiId, dependency.GetProperty("prerequisitePbiId").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewSendsTaskCreationSchemaThroughOllamaAndNeverRepairsFencedOutput(bool returnFences)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        using var handler = new SchemaAwareOllamaHandler(f.Story.PbiId, returnFences);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var chat = new OllamaApiClient(http);
        var service = Service(f, chat);
        var before = await f.Db.Stories.CountAsync();
        var request = new GenerateTaskCreationPreviewRequest([f.Story.PbiId], "schema-task-creation", ReplaceGeneratedTasks: true);

        if (returnFences)
        {
            await Assert.ThrowsAsync<JsonException>(() => service.PreviewAsync(f.ProjectId, request, "owner", default));
            Assert.Equal(GenerationState.Failed, (await f.Db.GenerationRuns.SingleAsync()).State);
            Assert.Equal(nameof(JsonException), (await f.Db.GenerationRuns.SingleAsync()).ErrorCode);
            using var input = JsonDocument.Parse((await f.Db.GenerationInputs.SingleAsync()).SnapshotJson);
            Assert.False(input.RootElement.TryGetProperty("Preview", out _));
        }
        else
        {
            var preview = await service.PreviewAsync(f.ProjectId, request, "owner", default);
            Assert.Equal(f.Story.PbiId, Assert.Single(preview.Tasks).ParentPbiId);
            Assert.Equal(GenerationState.Succeeded, (await f.Db.GenerationRuns.SingleAsync()).State);
        }

        Assert.Equal(before, await f.Db.Stories.CountAsync());
        Assert.Equal(1, handler.Calls);
        var schema = handler.Request.GetProperty("format");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var tasks = schema.GetProperty("properties").GetProperty("tasks");
        Assert.Equal("array", tasks.GetProperty("type").GetString());
        var task = tasks.GetProperty("items");
        Assert.False(task.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["key", "parentPbiId", "title", "description", "dependsOnKeys", "existingPrerequisites"],
            task.GetProperty("required").EnumerateArray().Select(property => property.GetString()));
    }

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
    public async Task Accept_PersistsGeneratedSpecificationOverTwoThousandCharacters()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var description = new string('x', 3000);
        var handler = new TaskCreationAiHandler();
        handler.SetOutput(new TaskCreationAiTask("long", f.Story.PbiId,
            "Implement detailed task", description, [], []));
        using var http = new HttpClient(handler);

        var preview = await Service(f, http).PreviewAsync(f.ProjectId,
            new([f.Story.PbiId], "long-task-specification"), "owner", default);
        var created = await Service(f, http).AcceptAsync(f.ProjectId,
            new(preview.GenerationRunId, preview.Tasks), "owner", default);

        var task = Assert.Single(created);
        Assert.True(task.Description.Length > 2000);
        Assert.Contains(description, task.Description);
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

    [Fact]
    public async Task PreviewRegenerationRejectsReplacedGeneratedTaskAsExistingPrerequisite()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var handler = new TaskCreationAiHandler();
        handler.SetOutput(new TaskCreationAiTask("initial", f.Story.PbiId,
            "Initial task", "Implement initial behavior.", [], []));
        using var http = new HttpClient(handler);
        var service = Service(f, http);

        var initialPreview = await service.PreviewAsync(f.ProjectId,
            new([f.Story.PbiId], "initial-task-set"), "owner", default);
        var initialTask = Assert.Single(await service.AcceptAsync(f.ProjectId,
            new(initialPreview.GenerationRunId, initialPreview.Tasks), "owner", default));

        handler.SetOutput(new TaskCreationAiTask("replacement", f.Story.PbiId,
            "Replacement task", "Implement replacement behavior.", [], [initialTask.PbiId]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(f.ProjectId,
            new([f.Story.PbiId], "replacement-task-set", ReplaceGeneratedTasks: true), "owner", default));

        Assert.Contains(await f.Db.GenerationRuns.ToArrayAsync(),
            run => run.IdempotencyKey == "replacement-task-set" && run.State == GenerationState.Failed);
    }

    private static TaskCreationService Service(TaskPlanningFixture f, HttpClient http) =>
        Service(f, new PlanningTestChatClient(http));

    private static TaskCreationService Service(TaskPlanningFixture f, IChatClient chat)
    {
        var options = new AiOptions { Model = "fake-task-model" };
        return new(f.Db, new LocalPlanningAi(chat, options),
            new ProjectDependencyService(f.Db, f.Notifier, f.Registry), f.Notifier, options,
            NullLogger<TaskCreationService>.Instance, f.Inference);
    }

    private sealed class SchemaAwareOllamaHandler(int parentPbiId, bool returnFences) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public JsonElement Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/chat", request.RequestUri!.AbsolutePath);
            Calls++;
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Request = document.RootElement.Clone();
            var output = JsonSerializer.Serialize(new TaskCreationAiOutput(
                [new("worker", parentPbiId, "Implement worker", "Implement the requested worker behavior.", [], [])]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (returnFences || !Request.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.Object)
                output = "```json\n" + output + "\n```";
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    model = "fake-task-model",
                    created_at = "2026-11-17T00:00:00Z",
                    message = new { role = "assistant", content = output },
                    done = true,
                    done_reason = "stop"
                })
            };
        }
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
