using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class TaskGenerationServiceTests
{
    [Theory]
    [InlineData("malformed")]
    [InlineData("dangling")]
    public async Task Preview_InvalidAiOutputMarksRunFailedAndPersistsNoPreviewOrTasks(string invalid)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var handler = new PlanningAiHandler();
        var proposal = Proposal(f);
        handler.Output = invalid == "malformed" ? """{"tasks": """ :
            SerializeOutput(proposal with { DependsOnKeys = ["missing-task"] });
        using var http = new HttpClient(handler);
        var service = Service(f, http);
        var before = await f.Db.Stories.CountAsync();
        var request = new GenerateTaskPreviewRequest([f.Story.PbiId], f.Snapshot.RepositorySnapshotId,
            ["src/Worker.cs"], "invalid-output");

        if (invalid == "malformed")
            await Assert.ThrowsAsync<JsonException>(() => service.PreviewAsync(f.ProjectId, request, "owner", default));
        else
            await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(f.ProjectId, request, "owner", default));

        var run = await f.Db.GenerationRuns.SingleAsync();
        Assert.Equal(GenerationState.Failed, run.State);
        Assert.Equal(invalid == "malformed" ? nameof(JsonException) : nameof(ArgumentException), run.ErrorCode);
        Assert.NotNull(run.CompletedAt);
        var saved = await f.Db.GenerationInputs.SingleAsync();
        using var failedInput = JsonDocument.Parse(saved.SnapshotJson);
        Assert.Equal(JsonSerializer.Serialize(request), failedInput.RootElement.GetProperty("Request").GetRawText());
        Assert.False(string.IsNullOrWhiteSpace(failedInput.RootElement.GetProperty("Prompt").GetString()));
        Assert.True(failedInput.RootElement.TryGetProperty("Context", out _));
        Assert.False(failedInput.RootElement.TryGetProperty("Tasks", out _));
        Assert.Empty(await f.Db.TaskSpecificationRevisions.ToArrayAsync());
        Assert.Equal(before, await f.Db.Stories.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PreviewAsync(f.ProjectId, request, "owner", default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task PreviewAndAccept_RetriesAreIdempotentAndPersistParentLinkedDraftScopeAndDependencies()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var first = Proposal(f);
        var second = first with { Key = "second", Title = "Follow-up task", DependsOnKeys = [first.Key] };
        var handler = new PlanningAiHandler { Output = SerializeOutput(first, second) };
        using var http = new HttpClient(handler);
        var service = Service(f, http);
        var request = new GenerateTaskPreviewRequest([f.Story.PbiId], f.Snapshot.RepositorySnapshotId,
            ["src/Worker.cs"], "successful-output");
        var preview = await service.PreviewAsync(f.ProjectId, request, "owner", default);
        var retry = await service.PreviewAsync(f.ProjectId, request, "owner", default);
        Assert.Equal(preview.GenerationRunId, retry.GenerationRunId);
        Assert.Equal(1, handler.Calls);
        Assert.All(preview.Tasks, task => Assert.Equal(TaskSpecificationService.SourceText(f.Story), task.Content.SourceStorySnapshot));
        using var input = JsonDocument.Parse((await f.Db.GenerationInputs.SingleAsync()).SnapshotJson);
        Assert.Equal(JsonSerializer.Serialize(request), input.RootElement.GetProperty("Request").GetRawText());
        Assert.Contains("Decompose each selected story", input.RootElement.GetProperty("Prompt").GetString());
        var context = input.RootElement.GetProperty("Context");
        Assert.Equal(TaskPlanningFixture.SnapshotSha, context.GetProperty("CommitSha").GetString());
        Assert.Equal("class Worker { }", context.GetProperty("Source").GetProperty("src/Worker.cs").GetString());
        Assert.Equal(f.Story.PbiId, Assert.Single(context.GetProperty("Stories").EnumerateArray()).GetProperty("PbiId").GetInt32());
        Assert.Equal(f.TaskId, Assert.Single(context.GetProperty("ExistingTasks").EnumerateArray()).GetProperty("PbiId").GetInt32());
        Assert.Empty(context.GetProperty("Dependencies").EnumerateArray());

        var accept = new AcceptTaskProposalsRequest(preview.GenerationRunId, preview.Tasks, false);
        var accepted = await service.AcceptAsync(f.ProjectId, accept, "owner", default);
        var repeated = await service.AcceptAsync(f.ProjectId, accept, "owner", default);

        Assert.Equal(accepted.Select(x => x.PbiId).ToArray(), repeated.Select(x => x.PbiId).ToArray());
        Assert.Equal(2, await f.Db.Stories.CountAsync(x => x.GenerationRunId == preview.GenerationRunId));
        Assert.All(accepted, item =>
        {
            Assert.Equal(f.Story.PbiId, item.ParentStoryId);
            Assert.Equal(PbiType.Task, item.Type);
            Assert.Equal(PbiOrigin.AiGenerated, item.Origin);
            Assert.False(item.IsDraft);
        });
        var specs = await f.Db.TaskSpecificationRevisions.Include(x => x.AllowedFiles).ToArrayAsync();
        Assert.Equal(2, specs.Length);
        Assert.All(specs, spec =>
        {
            Assert.Equal(SpecificationApproval.Draft, spec.Approval);
            Assert.Equal(preview.GenerationRunId, spec.GenerationRunId);
            Assert.Equal(f.Snapshot.RepositorySnapshotId, spec.RepositorySnapshotId);
            Assert.Contains(spec.AllowedFiles, file => file.Category == AllowedFileCategory.Test);
        });
        var edge = await f.Db.PbiDependencies.SingleAsync();
        Assert.Equal(accepted[1].PbiId, edge.DependentPbiId);
        Assert.Equal(accepted[0].PbiId, edge.PrerequisitePbiId);
    }

    [Fact]
    public async Task Accept_DanglingSelectedPrerequisiteRollsBackTasksAndSpecifications()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var first = Proposal(f);
        var second = first with { Key = "second", DependsOnKeys = [first.Key] };
        var handler = new PlanningAiHandler { Output = SerializeOutput(first, second) };
        using var http = new HttpClient(handler);
        var service = Service(f, http);
        var preview = await service.PreviewAsync(f.ProjectId,
            new([f.Story.PbiId], f.Snapshot.RepositorySnapshotId, ["src/Worker.cs"], "partial-accept"), "owner", default);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AcceptAsync(f.ProjectId,
            new(preview.GenerationRunId, [preview.Tasks[1]], false), "owner", default));

        // Clear tracked objects left by a rolled-back unit of work; assert database state.
        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.Stories.AnyAsync(x => x.GenerationRunId == preview.GenerationRunId));
        Assert.Empty(await f.Db.TaskSpecificationRevisions.ToArrayAsync());
        Assert.Empty(await f.Db.TaskAllowedFiles.ToArrayAsync());
        Assert.Empty(await f.Db.PbiDependencies.ToArrayAsync());
        Assert.Equal(GenerationState.Succeeded, (await f.Db.GenerationRuns.SingleAsync()).State);
    }

    [Theory]
    [InlineData("paths")]
    [InlineData("stories")]
    [InlineData("snapshot")]
    public async Task Preview_ReusedKeyWithChangedRequestRejectsCachedProposal(string changed)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var handler = new PlanningAiHandler { Output = SerializeOutput(Proposal(f)) };
        using var http = new HttpClient(handler);
        var service = Service(f, http);
        var request = new GenerateTaskPreviewRequest([f.Story.PbiId], f.Snapshot.RepositorySnapshotId,
            ["src/Worker.cs"], "request-fingerprint");
        await service.PreviewAsync(f.ProjectId, request, "owner", default);
        var changedRequest = changed switch
        {
            "paths" => request with { SourcePaths = ["tests/WorkerTests.cs"] },
            "stories" => request with { StoryIds = [f.Story.PbiId, f.TaskId] },
            _ => request with { RepositorySnapshotId = f.Snapshot.RepositorySnapshotId + 1 }
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PreviewAsync(f.ProjectId, changedRequest, "owner", default));

        Assert.Contains("different generation inputs", error.Message);
        Assert.Equal(1, handler.Calls);
        Assert.Single(await f.Db.GenerationRuns.ToArrayAsync());
        Assert.Single(await f.Db.GenerationInputs.ToArrayAsync());
        Assert.Empty(await f.Db.TaskSpecificationRevisions.ToArrayAsync());
    }

    [Theory]
    [InlineData(GenerationKind.Stories, GenerationState.Succeeded)]
    [InlineData(GenerationKind.Tasks, GenerationState.Failed)]
    [InlineData(GenerationKind.Tasks, GenerationState.Running)]
    public async Task Accept_RejectsWrongKindOrUnsuccessfulRunWithoutCreatingWork(GenerationKind kind, GenerationState state)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var proposal = Proposal(f);
        var run = new GenerationRun
        {
            ProjectId = f.ProjectId,
            Kind = kind,
            State = state,
            Provider = "test",
            IdempotencyKey = "invalid-accept",
            ActorId = "owner"
        };
        f.Db.GenerationRuns.Add(run);
        await f.Db.SaveChangesAsync();
        f.Db.GenerationInputs.Add(new()
        {
            ProjectId = f.ProjectId,
            GenerationRunId = run.GenerationRunId,
            RepositorySnapshotId = f.Snapshot.RepositorySnapshotId,
            SnapshotJson = JsonSerializer.Serialize(new TaskGenerationPreviewDto(run.GenerationRunId,
                f.Snapshot.RepositorySnapshotId, f.Project.CurrentGuidelinesRevisionId, [proposal]))
        });
        await f.Db.SaveChangesAsync();
        using var http = new HttpClient(new PlanningAiHandler());

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(f, http).AcceptAsync(f.ProjectId,
            new(run.GenerationRunId, [proposal], false), "owner", default));

        Assert.Contains("successful task-generation run", error.Message);
        Assert.False(await f.Db.Stories.AnyAsync(x => x.GenerationRunId == run.GenerationRunId));
        Assert.Empty(await f.Db.TaskSpecificationRevisions.ToArrayAsync());
        Assert.Empty(await f.Db.PbiDependencies.ToArrayAsync());
    }

    private static TaskProposalDto Proposal(TaskPlanningFixture f) =>
        new("first", f.Story.PbiId, "Implement worker", TaskPlanningFixture.Content(), f.Manifest, [], []);

    private static string SerializeOutput(params TaskProposalDto[] tasks) =>
        JsonSerializer.Serialize(new TaskGenerationOutput(tasks), new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        });

    private static TaskGenerationService Service(TaskPlanningFixture f, HttpClient http)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OllamaBaseUrl"] = "http://127.0.0.1:11434",
            ["OllamaModel"] = "fake-planning-model"
        }).Build();
        return new(f.Db, new LocalPlanningAi(http, configuration), new RepositoryContextService(f.Db, f.GitHub),
            new ProjectDependencyService(f.Db, f.Notifier), f.Specs, f.Notifier, configuration,
            NullLogger<TaskGenerationService>.Instance);
    }

    private sealed class PlanningAiHandler : HttpMessageHandler
    {
        public string Output { get; set; } = "";
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("http://127.0.0.1:11434/api/generate", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = Output })
            });
        }
    }
}
