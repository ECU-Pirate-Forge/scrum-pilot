using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.ServiceTests;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

public sealed partial class TenantPipelineTests
{
    [Fact]
    public async Task PlanningHttp_SyntheticFeatureFlowPersistsSelectedWorkAndReconcilesSprintDependenciesAndScopedEvents()
    {
        var a = await files.Create("SyntheticWorkflowA");
        var b = await files.Create("SyntheticWorkflowB", "member");
        var projectId = await files.Project(a);
        Assert.Equal(projectId, await files.Project(b));
        var root = $"/api/organizations/{a.OrganizationId}/projects/{projectId}";
        var otherRoot = $"/api/organizations/{b.OrganizationId}/projects/{projectId}";
        var pbiRoute = $"/api/organizations/{a.OrganizationId}/pbi";
        var selectedStoryId = 0;
        var chatInputs = new List<string>();
        var chat = Substitute.For<IChatClient>();
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                chatInputs.Add(string.Join("\n", call.ArgAt<IEnumerable<ChatMessage>>(0).Select(message => message.Text)));
                var json = selectedStoryId == 0
                    ? """{"stories":[{"title":"Selected synthetic feature","description":"As a planner, I want verified child tasks, so that dependencies stay observable.\nAcceptance criteria:\n- Reopening a prerequisite requires downstream review.","priority":"Medium","storyPoints":"Three"},{"title":"Unselected alternative","description":"This candidate must remain a preview.","priority":"Low","storyPoints":"Zero"}]}"""
                    : JsonSerializer.Serialize(new TaskGenerationOutput([
                        new("prepare", selectedStoryId, "Prepare synthetic worker", TaskPlanningFixture.Content(),
                            WorkflowManifest(), [], []),
                        new("finish", selectedStoryId, "Finish synthetic worker", TaskPlanningFixture.Content(),
                            WorkflowManifest(), ["prepare"], [])
                    ]), new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        Converters = { new JsonStringEnumConverter() }
                    });
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, json));
            });
        var githubResponses = new PlanningGitHubHandler();
        using var githubHttp = new HttpClient(githubResponses);
        using var signingKey = RSA.Create(2048);
        var github = new GitHubAppClient(githubHttp, Options.Create(new GitHubAppOptions
        {
            AppId = "123",
            PrivateKeyPem = signingKey.ExportPkcs8PrivateKeyPem()
        }));
        serviceOverrides = services =>
        {
            services.AddSingleton<IChatClient>(chat);
            services.AddScoped<GitHubAppClient>(_ => github);
        };
        using var owner = Client("owner");
        using var otherOwner = Client("member");
        await using var updatesA = WorkflowWorkConnection(a.OrganizationId, "owner");
        await using var updatesB = WorkflowWorkConnection(b.OrganizationId, "member");
        var eventsA = Channel.CreateUnbounded<(int ProjectId, long Revision)>();
        var eventsB = Channel.CreateUnbounded<(int ProjectId, long Revision)>();
        using var callbackA = updatesA.On<int, long>("ProjectWorkChanged", (id, revision) => eventsA.Writer.TryWrite((id, revision)));
        using var callbackB = updatesB.On<int, long>("ProjectWorkChanged", (id, revision) => eventsB.Writer.TryWrite((id, revision)));
        await updatesA.StartAsync();
        await updatesB.StartAsync();
        await updatesA.InvokeAsync("Subscribe", projectId);
        await updatesB.InvokeAsync("Subscribe", projectId);

        var guidance = await WorkflowJsonAsync<ProjectGuidelinesRevision>(await owner.PutAsJsonAsync(root + "/guidelines",
            new SaveProjectGuidelinesRequest("Synthetic workflow guidelines: retain exact tests and documentation.", null)));
        var source = await WorkflowJsonAsync<ProblemStatement>(await owner.PostAsJsonAsync(root + "/problem-statements",
            new SaveProblemStatementRequest("Synthetic planning problem", "Keep prerequisite completion and downstream review observable.", null, false)));
        var batch = await WorkflowJsonAsync<ProjectStoryBatchResult>(await owner.PostAsJsonAsync(root + "/story-generation",
            new GenerateProjectStoriesRequest("http-feature-stories", [source.ProblemStatementId])));
        var run = Assert.Single(batch.Results);
        Assert.Equal(GenerationState.Succeeded, run.State);
        Assert.Equal(2, run.Proposals.Count);
        var selected = run.Proposals[0];
        var accepted = await WorkflowJsonAsync<ProductBacklogItem[]>(await owner.PostAsJsonAsync(root + "/story-generation/accept",
            new AcceptProjectStoriesRequest(run.GenerationRunId!.Value, false, [
                new(selected.ProposalId, 0, selected.Pbi.Title, selected.Pbi.Description, selected.Pbi.Priority, selected.Pbi.StoryPoints)
            ])));
        var story = Assert.Single(accepted);
        selectedStoryId = story.PbiId;
        Assert.Equal(source.ProblemStatementId, story.ProblemStatementId);
        Assert.Equal(run.GenerationRunId, story.GenerationRunId);
        Assert.False(story.IsDraft);
        Assert.Equal(PbiOrigin.AiGenerated, story.Origin);
        var persisted = await owner.GetFromJsonAsync<ProductBacklogItem[]>(pbiRoute + $"/getAllPbis?projectId={projectId}");
        Assert.Equal(story.PbiId, Assert.Single(persisted!).PbiId);
        Assert.DoesNotContain(persisted!, item => item.Title == "Unselected alternative");

        var sprint = await WorkflowJsonAsync<Sprint>(await owner.PostAsJsonAsync(
            $"/api/organizations/{a.OrganizationId}/sprint?projectId={projectId}",
            new Sprint { SprintTitle = "Synthetic sprint", IsOpen = true }));
        story.SprintId = sprint.SprintId;
        story = await WorkflowJsonAsync<ProductBacklogItem>(await owner.PutAsJsonAsync(pbiRoute, story));
        await WorkflowJsonAsync<ScrumPilot.Shared.Models.ProjectRepository>(await owner.PutAsJsonAsync(root + "/repository",
            new ConfigureProjectRepositoryRequest(99, 42, "owner", "repository", "release/planning", 0)));
        var snapshot = await WorkflowJsonAsync<RepositorySnapshot>(await owner.PostAsJsonAsync(root + "/repository/snapshots",
            new CreateRepositorySnapshotRequest("release/planning")));
        var preview = await WorkflowJsonAsync<TaskGenerationPreviewDto>(await owner.PostAsJsonAsync(root + "/task-generation/preview",
            new GenerateTaskPreviewRequest([story.PbiId], snapshot.RepositorySnapshotId, ["src/Worker.cs"], "http-feature-tasks")));
        Assert.Equal(2, preview.Tasks.Length);
        var tasks = await WorkflowJsonAsync<ProductBacklogItem[]>(await owner.PostAsJsonAsync(root + "/task-generation/accept",
            new AcceptTaskProposalsRequest(preview.GenerationRunId, preview.Tasks, false)));
        Assert.Equal(2, tasks.Length);
        Assert.All(tasks, task =>
        {
            Assert.Equal(story.PbiId, task.ParentStoryId);
            Assert.Equal(sprint.SprintId, task.SprintId);
            Assert.Equal(preview.GenerationRunId, task.GenerationRunId);
        });
        foreach (var task in tasks)
        {
            var specification = await owner.GetFromJsonAsync<TaskSpecificationStatusDto>(root + $"/tasks/{task.PbiId}/specifications");
            Assert.Equal(guidance.ProjectGuidelinesRevisionId, specification!.Specification!.ProjectGuidelinesRevisionId);
            Assert.Equal(snapshot.RepositorySnapshotId, specification.Specification.RepositorySnapshotId);
            Assert.Equal(SpecificationApproval.Draft, specification.Specification.Approval);
            Assert.Contains(specification.Specification.AllowedFiles, file => file.Category == AllowedFileCategory.Test);
        }

        var graphRoute = root + $"/sprints/{sprint.SprintId}/dependency-graph";
        var graph = (await owner.GetFromJsonAsync<DependencyGraphDto>(graphRoute))!;
        var predecessor = tasks[0];
        var dependent = tasks[1];
        Assert.Equal("Ready", Node(predecessor.PbiId).State);
        Assert.Equal("Blocked", Node(dependent.PbiId).State);
        Assert.Contains(graph.Edges, edge => edge.DependentId == dependent.PbiId && edge.PrerequisiteId == predecessor.PbiId);
        Assert.False(Node(predecessor.PbiId).ImplementationEligible); // Draft scope is not Owner approval.
        foreach (var task in tasks)
        {
            var approved = await WorkflowJsonAsync<TaskSpecificationRevision>(await owner.PostAsJsonAsync(
                root + $"/tasks/{task.PbiId}/specifications/approve", new ApproveTaskSpecificationRequest(1)));
            Assert.Equal(SpecificationApproval.Approved, approved.Approval);
        }
        var scopeCheck = await WorkflowJsonAsync<PullRequestScopeCheck>(await owner.PostAsJsonAsync(
            root + $"/tasks/{predecessor.PbiId}/pull-requests", new BindTaskPullRequestRequest(7)));
        Assert.Equal(ScopeCheckState.Passed, scopeCheck.State);
        graph = (await owner.GetFromJsonAsync<DependencyGraphDto>(graphRoute))!;
        Assert.True(Node(predecessor.PbiId).ImplementationEligible);
        predecessor.Status = PbiStatus.Done;
        predecessor = await WorkflowJsonAsync<ProductBacklogItem>(await owner.PutAsJsonAsync(pbiRoute, predecessor));
        graph = (await owner.GetFromJsonAsync<DependencyGraphDto>(graphRoute))!;
        Assert.Equal("Ready", Node(dependent.PbiId).State);
        dependent.Status = PbiStatus.InProgress;
        dependent = await WorkflowJsonAsync<ProductBacklogItem>(await owner.PutAsJsonAsync(pbiRoute, dependent));
        predecessor.Status = PbiStatus.ToDo;
        predecessor = await WorkflowJsonAsync<ProductBacklogItem>(await owner.PutAsJsonAsync(pbiRoute, predecessor));
        graph = (await owner.GetFromJsonAsync<DependencyGraphDto>(graphRoute))!;
        Assert.Equal("Ready", Node(predecessor.PbiId).State);
        Assert.Equal("Needs review", Node(dependent.PbiId).State);
        Assert.Equal(PbiStatus.InProgress, Node(dependent.PbiId).Item.Status);
        Assert.False(Node(dependent.PbiId).CompletionSatisfied);
        Assert.False(Node(dependent.PbiId).DependencyReady);
        predecessor.Status = PbiStatus.Done;
        await WorkflowJsonAsync<ProductBacklogItem>(await owner.PutAsJsonAsync(pbiRoute, predecessor));
        var review = Assert.Single((await owner.GetFromJsonAsync<DependencyReview[]>(root + "/dependency-reviews"))!);
        Assert.Equal(dependent.PbiId, review.PbiId);
        using (var reviewed = await owner.PostAsJsonAsync(root + "/dependencies/review",
            new ReviewDependencyRequest(dependent.PbiId, review.InvalidationRevision)))
            Assert.Equal(HttpStatusCode.NoContent, reviewed.StatusCode);
        graph = (await owner.GetFromJsonAsync<DependencyGraphDto>(graphRoute))!;
        Assert.Equal("Active", Node(dependent.PbiId).State);
        dependent.Status = PbiStatus.ToDo;
        await WorkflowJsonAsync<ProductBacklogItem>(await owner.PutAsJsonAsync(pbiRoute, dependent));
        graph = (await owner.GetFromJsonAsync<DependencyGraphDto>(graphRoute))!;
        Assert.Equal("Ready", Node(dependent.PbiId).State);
        Assert.True(Node(dependent.PbiId).ImplementationEligible);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        (int ProjectId, long Revision) changed;
        do { changed = await eventsA.Reader.ReadAsync(timeout.Token); } while (changed.Revision != graph.Revision);
        Assert.Equal(projectId, changed.ProjectId);
        Assert.False(eventsB.Reader.TryRead(out _));
        await WorkflowJsonAsync<ProjectGuidelinesRevision>(await otherOwner.PutAsJsonAsync(otherRoot + "/guidelines",
            new SaveProjectGuidelinesRequest("Other organization's independent guidance.", null)));
        var otherGraph = (await otherOwner.GetFromJsonAsync<DependencyGraphDto>(otherRoot + "/dependency-graph"))!;
        var otherEvent = await eventsB.Reader.ReadAsync(timeout.Token);
        Assert.Equal(projectId, otherEvent.ProjectId);
        Assert.Equal(otherGraph.Revision, otherEvent.Revision);
        Assert.Empty(otherGraph.Nodes);
        Assert.Equal(2, chatInputs.Count);
        Assert.All(chatInputs, input => Assert.Contains("Synthetic workflow guidelines", input));
        Assert.Contains("Keep prerequisite completion", chatInputs[0]);
        Assert.NotEmpty(githubResponses.Requests);
        DependencyGraphNodeDto Node(int id) => graph.Nodes.Single(node => node.Item.PbiId == id);
    }

    private Microsoft.AspNetCore.SignalR.Client.HubConnection WorkflowWorkConnection(int organizationId, string user) =>
        new HubConnectionBuilder().WithUrl($"https://localhost/hubs/organizations/{organizationId}/project-work", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.HttpMessageHandlerFactory = _ => application!.Server.CreateHandler();
            options.Headers["X-Test-User"] = user;
            options.WebSocketFactory = async (context, ct) =>
            {
                var client = application!.Server.CreateWebSocketClient();
                client.ConfigureRequest = request => request.Headers["X-Test-User"] = user;
                return await client.ConnectAsync(context.Uri, ct);
            };
        }).Build();

    private static TaskFileProposal[] WorkflowManifest() =>
    [
        new("src/Worker.cs", AllowedFileCategory.Source, AllowedFileOperation.Modify, "Implement the synthetic worker."),
        new("tests/WorkerTests.cs", AllowedFileCategory.Test, AllowedFileOperation.Modify, "Verify the synthetic worker."),
        new("docs/task.md", AllowedFileCategory.Documentation, AllowedFileOperation.Modify, "Document the synthetic worker.")
    ];

    private static async Task<T> WorkflowJsonAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
    }

    [Fact]
    public async Task PlanningHttp_CollidingIdsReadOnlyTheExplicitAuthorizedOrganization()
    {
        var a = await SeedPlanningAsync(await files.Create("PlanningA"));
        var b = await SeedPlanningAsync(await files.Create("PlanningB", "member"));
        Assert.Equal(a.ProjectId, b.ProjectId);
        Assert.Equal(a.TaskId, b.TaskId);
        Assert.Equal(a.ProblemId, b.ProblemId);
        Assert.Equal(a.GuidelinesId, b.GuidelinesId);
        Assert.Equal(a.SpecificationId, b.SpecificationId);

        using var owner = Client("owner");
        using var member = Client("member");
        using var admin = Client("admin");
        await AssertPlanningReadsAsync(owner, a);
        await AssertPlanningReadsAsync(member, b);
        await AssertPlanningDeniedAsync(owner, b, HttpStatusCode.NotFound);
        await AssertPlanningDeniedAsync(member, a, HttpStatusCode.NotFound);
        await AssertPlanningDeniedAsync(admin, a, HttpStatusCode.NotFound);
        await AssertPlanningDeniedAsync(admin, b, HttpStatusCode.NotFound);

        // The same authenticated client can read both colliding local IDs only after
        // deliberate central membership and a current-instance project grant.
        var membership = new OrganizationMembership
        {
            OrganizationId = b.Organization.OrganizationId,
            UserId = "owner",
            Role = OrganizationRole.Member
        };
        files.Control.OrganizationMemberships.Add(membership);
        await files.Control.SaveChangesAsync();
        await AssertPlanningDeniedAsync(owner, b, HttpStatusCode.NotFound);
        await GrantPlanningProjectAsync(b, membership);
        await AssertPlanningReadsAsync(owner, b);
        await AssertPlanningReadsAsync(owner, a);

        using var legacy = await owner.GetAsync($"/api/projects/{a.ProjectId}/guidelines");
        Assert.Equal(HttpStatusCode.NotFound, legacy.StatusCode);
    }

    [Fact]
    public async Task PlanningHttp_ConflictingBodyOrganizationCannotMutateEitherCatalog()
    {
        var a = await SeedPlanningAsync(await files.Create("BodyA"));
        var b = await SeedPlanningAsync(await files.Create("BodyB", "member"));
        using var owner = Client("owner");
        using var problem = await owner.PostAsJsonAsync(a.Root + "/problem-statements", new
        {
            organizationId = b.Organization.OrganizationId,
            title = "Must not be written",
            text = "Must not be written"
        });
        Assert.Equal(HttpStatusCode.BadRequest, problem.StatusCode);
        using var guidance = await owner.PutAsJsonAsync(a.Root + "/guidelines", new
        {
            organizationId = b.Organization.OrganizationId,
            content = "Must not be written",
            expectedRevisionId = a.GuidelinesId
        });
        Assert.Equal(HttpStatusCode.BadRequest, guidance.StatusCode);
        using var ownerRead = await owner.GetAsync(a.Root + "/problem-statements");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        await AssertPlanningUnchangedAsync(a);
        await AssertPlanningUnchangedAsync(b);
    }

    [Fact]
    public async Task PlanningHttp_RemovedAndRejoinedMemberCannotReuseThePreviousProjectGrant()
    {
        var seed = await SeedPlanningAsync(await files.Create("RejoinedPlanning"));
        var membership = new OrganizationMembership
        {
            OrganizationId = seed.Organization.OrganizationId,
            UserId = "member",
            Role = OrganizationRole.Member
        };
        files.Control.OrganizationMemberships.Add(membership);
        await files.Control.SaveChangesAsync();
        await GrantPlanningProjectAsync(seed, membership);
        using var member = Client("member");
        await AssertPlanningReadsAsync(member, seed);

        files.Control.OrganizationMemberships.Remove(membership);
        await files.Control.SaveChangesAsync();
        await AssertPlanningDeniedAsync(member, seed, HttpStatusCode.NotFound);
        var replacement = new OrganizationMembership
        {
            OrganizationId = seed.Organization.OrganizationId,
            UserId = "member",
            Role = OrganizationRole.Member
        };
        files.Control.OrganizationMemberships.Add(replacement);
        await files.Control.SaveChangesAsync();
        Assert.NotEqual(membership.MembershipInstanceId, replacement.MembershipInstanceId);
        await AssertPlanningDeniedAsync(member, seed, HttpStatusCode.NotFound);
        using (var tenant = files.Storage.Open(seed.Organization))
        {
            var stale = await tenant.ProjectMemberships.SingleAsync();
            Assert.Equal(membership.MembershipInstanceId, stale.MembershipInstanceId);
            tenant.ProjectMemberships.Remove(stale);
            await tenant.SaveChangesAsync();
        }
        await GrantPlanningProjectAsync(seed, replacement);
        await AssertPlanningReadsAsync(member, seed);
        await AssertPlanningUnchangedAsync(seed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlanningHttp_MaintenanceAndWrongCatalogFailBeforePlanningWork(bool wrongCatalog)
    {
        var a = await SeedPlanningAsync(await files.Create("UnavailablePlanningA"));
        var b = await SeedPlanningAsync(await files.Create("UnavailablePlanningB", "member"));
        var aKey = a.Organization.StorageKey;
        var bKey = b.Organization.StorageKey;
        if (wrongCatalog)
        {
            // Corrupt only this fixture's central registry: both real catalogs remain
            // intact, but each row points at the other organization's metadata.
            a.Organization.StorageKey = Guid.NewGuid().ToString("N");
            await files.Control.SaveChangesAsync();
            b.Organization.StorageKey = aKey;
            await files.Control.SaveChangesAsync();
            a.Organization.StorageKey = bKey;
            await files.Control.SaveChangesAsync();
        }
        else
        {
            a.Organization.StorageState = "Maintenance";
            await files.Control.SaveChangesAsync();
        }

        using var owner = Client("owner");
        using var admin = Client("admin");
        await AssertPlanningDeniedAsync(owner, a, HttpStatusCode.ServiceUnavailable);
        await AssertPlanningDeniedAsync(admin, a, HttpStatusCode.NotFound);
        using var attemptedWrite = await owner.PostAsJsonAsync(a.Root + "/problem-statements",
            new SaveProblemStatementRequest("Must not be written", "Must not be written", null, false));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, attemptedWrite.StatusCode);
        // Open the fixture-owned original catalogs directly for observation only.
        // Ordinary HTTP must never do this or silently choose the other catalog.
        await AssertPlanningUnchangedAsync(a, aKey);
        await AssertPlanningUnchangedAsync(b, bKey);
    }

    private async Task<PlanningSeed> SeedPlanningAsync(Organization organization)
    {
        var projectId = await files.Project(organization, organization.Name);
        using var tenant = files.Storage.Open(organization);
        var guidance = new ProjectGuidelinesRevision
        {
            ProjectId = projectId,
            Revision = 1,
            Content = organization.Name + " guidance",
            ActorId = "owner"
        };
        var problem = new ProblemStatement
        {
            ProjectId = projectId,
            Title = organization.Name + " problem",
            Text = organization.Name + " requirements"
        };
        var task = new ProductBacklogItem
        {
            ProjectId = projectId,
            Title = organization.Name + " task",
            Description = organization.Name + " work",
            Type = PbiType.Task,
            Status = PbiStatus.ToDo
        };
        tenant.AddRange(guidance, problem, task);
        await tenant.SaveChangesAsync();
        var project = await tenant.Projects.SingleAsync();
        project.CurrentGuidelinesRevisionId = guidance.ProjectGuidelinesRevisionId;
        // A pre-existing advisory revision exercises real specification reads without
        // invoking GitHub or manufacturing an Owner-approved enforced scope.
        var specification = new TaskSpecificationRevision
        {
            ProjectId = projectId,
            PbiId = task.PbiId,
            Revision = 1,
            ActorId = "owner",
            ScopeMode = TaskScopeMode.Advisory,
            Approval = SpecificationApproval.Draft,
            ProjectGuidelinesRevisionId = guidance.ProjectGuidelinesRevisionId,
            Content = new()
            {
                Goal = organization.Name + " specification",
                SourceStoryVersion = task.Version,
                SourceStorySnapshot = TaskSpecificationService.SourceText(task)
            }
        };
        tenant.TaskSpecificationRevisions.Add(specification);
        await tenant.SaveChangesAsync();
        return new(organization, projectId, task.PbiId, problem.ProblemStatementId,
            guidance.ProjectGuidelinesRevisionId, specification.TaskSpecificationRevisionId);
    }

    private async Task GrantPlanningProjectAsync(PlanningSeed seed, OrganizationMembership membership)
    {
        using var tenant = files.Storage.Open(seed.Organization);
        tenant.ProjectMemberships.Add(new()
        {
            ProjectId = seed.ProjectId,
            UserId = membership.UserId,
            GrantedByUserId = "owner",
            MembershipInstanceId = membership.MembershipInstanceId
        });
        await tenant.SaveChangesAsync();
    }

    private static async Task AssertPlanningReadsAsync(HttpClient client, PlanningSeed seed)
    {
        var guidance = await client.GetFromJsonAsync<ProjectGuidelinesRevision>(seed.Root + "/guidelines");
        Assert.Equal(seed.GuidelinesId, guidance!.ProjectGuidelinesRevisionId);
        Assert.Equal(seed.Organization.Name + " guidance", guidance.Content);
        var problem = Assert.Single((await client.GetFromJsonAsync<List<ProblemStatement>>(seed.Root + "/problem-statements"))!);
        Assert.Equal(seed.ProblemId, problem.ProblemStatementId);
        Assert.Equal(seed.Organization.Name + " requirements", problem.Text);
        var specification = await client.GetFromJsonAsync<TaskSpecificationStatusDto>(
            seed.Root + $"/tasks/{seed.TaskId}/specifications");
        Assert.NotNull(specification!.Specification);
        Assert.Equal(seed.SpecificationId, specification.Specification.TaskSpecificationRevisionId);
        Assert.Equal(seed.Organization.Name + " specification", specification.Specification.Content.Goal);
        var graph = await client.GetFromJsonAsync<DependencyGraphDto>(seed.Root + "/dependency-graph");
        Assert.Equal(seed.ProjectId, graph!.ProjectId);
        var node = Assert.Single(graph.Nodes);
        Assert.Equal(seed.TaskId, node.Item.PbiId);
        Assert.Equal(seed.Organization.Name + " task", node.Item.Title);
    }

    private static async Task AssertPlanningDeniedAsync(HttpClient client, PlanningSeed seed, HttpStatusCode expected)
    {
        foreach (var suffix in new[] { "/guidelines", "/problem-statements", $"/tasks/{seed.TaskId}/specifications", "/dependency-graph" })
        {
            using var response = await client.GetAsync(seed.Root + suffix);
            Assert.Equal(expected, response.StatusCode);
            Assert.DoesNotContain(seed.Organization.Name, await response.Content.ReadAsStringAsync());
        }
    }

    private async Task AssertPlanningUnchangedAsync(PlanningSeed seed, string? originalStorageKey = null)
    {
        using var tenant = originalStorageKey is null ? files.Storage.Open(seed.Organization) : files.Storage.OpenFile(originalStorageKey);
        Assert.Equal(seed.Organization.Name + " requirements", (await tenant.ProblemStatements.SingleAsync()).Text);
        Assert.Equal(seed.Organization.Name + " guidance", (await tenant.ProjectGuidelinesRevisions.SingleAsync()).Content);
        Assert.Equal(seed.Organization.Name + " specification", (await tenant.TaskSpecificationRevisions.SingleAsync()).Content.Goal);
        Assert.Equal(seed.Organization.Name + " task", (await tenant.Stories.SingleAsync()).Title);
    }

    private sealed record PlanningSeed(Organization Organization, int ProjectId, int TaskId, int ProblemId,
        int GuidelinesId, int SpecificationId)
    {
        public string Root => $"/api/organizations/{Organization.OrganizationId}/projects/{ProjectId}";
    }
}
