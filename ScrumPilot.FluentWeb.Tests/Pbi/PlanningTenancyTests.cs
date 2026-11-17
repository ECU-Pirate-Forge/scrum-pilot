using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Client;
using ScrumPilot.FluentWeb.Components.DependencyChart;
using ScrumPilot.FluentWeb.Components.Pbi;
using ScrumPilot.FluentWeb.Components.ProjectManagement;
using ScrumPilot.FluentWeb.Pages;
using ScrumPilot.FluentWeb.Layout;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.FluentWeb.Tests.TestHelpers;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Pbi;

public sealed class PlanningTenancyTests : FluentFrontendTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Task_planning_navigation_is_available_independently_of_github(bool enabled)
    {
        Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FeatureOptions>>().Value.GitHubIntegration = enabled;
        using var menu = Render<NavMenu>();
        Assert.Contains("task-planning", menu.Markup);
    }

    [Fact]
    public async Task Disabled_github_allows_task_preview_for_a_selected_story()
    {
        Http.OnGetJson("api/features", new { GitHubIntegrationEnabled = false });
        Http.OnGetJson("api/organizations/2/Pbi/getAllPbis", new[]
        {
            new ProductBacklogItem { PbiId = 5, ProjectId = 17, Title = "Scoped story", Type = PbiType.Story }
        });
        Http.On(HttpMethod.Post, "api/organizations/2/projects/17/task-creation/preview", (_, _) =>
            MockHttpHandler.Json(new TaskCreationPreviewDto(7, null,
                [new("task-1", 5, "Draft task", "Implement it", [], [], 1, "story")])));
        Http.On(HttpMethod.Post, "api/organizations/2/projects/17/task-creation/accept", (_, _) =>
            MockHttpHandler.Json(Array.Empty<ProductBacklogItem>()));
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        using var page = Render<TaskPlanning>(p => p.Add(x => x.CapturedWork, scope));
        page.WaitForAssertion(() => Assert.Contains("Scoped story", page.Markup));
        page.Find("select").Change("5");
        await page.FindAll("fluent-button").Single(x => x.TextContent.Contains("Generate task preview")).ClickAsync(new());
        page.WaitForAssertion(() => Assert.Contains("Review and edit proposals", page.Markup));
        page.Find("input[type=checkbox]").Change(true);
        await page.FindAll("fluent-button").Single(x => x.TextContent.Contains("Save selected task proposals")).ClickAsync(new());
        Assert.Contains(Http.Requests, request => request.Method == HttpMethod.Post
            && request.Path == "api/organizations/2/projects/17/task-creation/preview");
        Assert.Contains(Http.Requests, request => request.Method == HttpMethod.Post
            && request.Path == "api/organizations/2/projects/17/task-creation/accept");
        Assert.DoesNotContain(Http.Requests, request => request.Path.Contains("/repository"));
    }

    [Fact]
    public async Task Disabled_github_allows_manual_task_creation_for_a_bug()
    {
        Http.OnGetJson("api/features", new { GitHubIntegrationEnabled = false });
        Http.OnGetJson("api/organizations/2/Pbi/getAllPbis", new[]
        {
            new ProductBacklogItem { PbiId = 8, ProjectId = 17, Title = "Scoped bug", Type = PbiType.Bug }
        });
        Http.On(HttpMethod.Post, "api/organizations/2/projects/17/task-creation", (_, _) =>
            MockHttpHandler.Json(new ProductBacklogItem { Title = "Fix bug" }));
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        using var page = Render<TaskPlanning>(p => p.Add(x => x.CapturedWork, scope));
        page.WaitForAssertion(() => Assert.Contains("Scoped bug", page.Markup));
        page.Find("select").Change("8");
        page.Find("section[aria-label='Add a task manually'] input").Change("Fix bug");
        page.Find("section[aria-label='Add a task manually'] textarea").Change("Repair behavior");
        await page.FindAll("fluent-button").Single(x => x.TextContent.Contains("Add draft task")).ClickAsync(new());
        var request = Assert.Single(Http.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal("api/organizations/2/projects/17/task-creation", request.Path);
        Assert.Contains("\"parentPbiId\":8", request.Body);
    }

    [Fact]
    public async Task Removing_a_child_task_uses_the_captured_organization()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        Http.OnGetJson("api/features", new { GitHubIntegrationEnabled = false });
        Http.OnGetJson("api/organizations/2/Pbi/getAllPbis", new[]
        {
            new ProductBacklogItem { PbiId = 5, ProjectId = 17, Title = "Scoped story", Type = PbiType.Story },
            new ProductBacklogItem { PbiId = 9, ProjectId = 17, Title = "Child task", Type = PbiType.Task, ParentStoryId = 5 }
        });
        Http.OnStatus(HttpMethod.Delete, "api/organizations/2/projects/17/task-creation/9", HttpStatusCode.NoContent);
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        using var page = Render<TaskPlanning>(p => p.Add(x => x.CapturedWork, scope));
        page.WaitForAssertion(() => Assert.Contains("Scoped story", page.Markup));
        page.Find("select").Change("5");
        await page.FindAll("fluent-button").Single(x => x.TextContent.Contains("Remove task")).ClickAsync(new());
        Assert.Contains(Http.Requests, request => request.Method == HttpMethod.Delete
            && request.Path == "api/organizations/2/projects/17/task-creation/9");
    }

    [Fact]
    public void Task_planning_uses_captured_tenant_and_project_not_mutable_selections()
    {
        Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FeatureOptions>>().Value.GitHubIntegration = true;
        Services.GetRequiredService<ProjectStateService>().SetProject(new Project { ProjectId = 99, ProjectName = "Other selection" });
        Services.GetRequiredService<OrganizationStateService>().SetOrganization(new(1, "Other tenant", OrganizationRole.Owner, false));
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        Http.OnGetJson("api/organizations/2/Pbi/getAllPbis", new[]
        {
            new ProductBacklogItem { PbiId = 5, ProjectId = 17, Title = "Scoped story", Type = PbiType.Story }
        });
        Http.OnGetJson("api/organizations/2/projects/17/repository/snapshots", Array.Empty<RepositorySnapshot>());
        Http.OnGetJson("api/organizations/2/projects/17/repository", new ProjectRepositoryStatus(null, false, "Not configured"));
        Http.OnGetJson("api/features", new { GitHubIntegrationEnabled = true });
        using var cut = Render<TaskPlanning>(p => p.Add(x => x.CapturedWork, scope));
        using var menu = Render<NavMenu>();
        Assert.Contains("task-planning", menu.Markup);
        cut.WaitForAssertion(() => Assert.Contains("Scoped story", cut.Markup));
        Assert.All(Http.Requests, request => Assert.True(request.Path == "api/features"
            || request.Path.StartsWith("api/organizations/2/", StringComparison.Ordinal)));
        Assert.Contains(Http.Requests, request => request.Path.EndsWith("/pbi/getallpbis") && request.Query == "?projectId=17");
        Assert.True(cut.FindAll("fluent-button").Single(x => x.TextContent.Contains("Verify and save repository")).HasAttribute("disabled"));
        Assert.True(OrganizationRoutes.IsWorkPage(new Uri("https://gateway/fluentweb/organizations/2/task-planning?projectId=17")));
        Assert.Equal("hubs/organizations/2/project-work", OrganizationRoutes.ProjectWorkHub(2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retired_guidelines_cannot_replace_equal_project_ids_or_report_old_errors(bool success)
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Http.OnAsync(HttpMethod.Get, "api/organizations/1/projects/17/guidelines", (_, _) => delayed.Task);
        Http.OnGetJson("api/organizations/2/projects/17/guidelines", new ProjectGuidelinesRevision
        { ProjectId = 17, Revision = 2, ActorId = "owner", Content = "Current tenant guidance" });
        Http.OnGetJson("api/organizations/2/projects/17/guidelines/history", Array.Empty<ProjectGuidelinesRevision>());
        using var client = Http.CreateClient();
        using var first = new WorkScope(client, 1, 17);
        using var second = new WorkScope(client, 2, 17);
        using var old = Render<ProjectGuidelinesPanel>(p => p.Add(x => x.ProjectId, 17).Add(x => x.CapturedWork, first));
        first.Dispose();
        using var current = Render<ProjectGuidelinesPanel>(p => p.Add(x => x.ProjectId, 17).Add(x => x.CapturedWork, second));
        current.WaitForAssertion(() => Assert.Contains("Current tenant guidance", current.Markup));
        delayed.SetResult(success ? MockHttpHandler.Json(new ProjectGuidelinesRevision
        { ProjectId = 17, Revision = 1, ActorId = "owner", Content = "Retired tenant guidance" })
            : new HttpResponseMessage(HttpStatusCode.Forbidden));
        await old.InvokeAsync(() => Task.CompletedTask);
        Assert.DoesNotContain("Retired tenant guidance", old.Markup + current.Markup);
        Assert.DoesNotContain("Could not load guidelines", old.Markup + current.Markup);
        Assert.True(second.IsCurrent);
    }

    [Fact]
    public async Task Removed_editor_cancels_its_own_work_without_canceling_page_scope()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Http.OnAsync(HttpMethod.Get, "api/organizations/2/projects/17/guidelines", (_, _) => delayed.Task);
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        var cut = Render<ProjectGuidelinesPanel>(p => p.Add(x => x.ProjectId, 17).Add(x => x.CapturedWork, scope));
        await DisposeComponentsAsync();
        delayed.SetResult(MockHttpHandler.Json(new ProjectGuidelinesRevision
        { ProjectId = 17, Revision = 1, ActorId = "owner", Content = "Late editor" }));
        await Task.Yield();
        Assert.True(scope.IsCurrent);
        Assert.DoesNotContain(Http.Requests, request => request.Path.EndsWith("/history"));
    }

    [Fact]
    public async Task Late_project_proposal_acceptance_does_not_emit_saved_callbacks()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Http.OnAsync(HttpMethod.Post, "api/organizations/2/projects/17/story-generation/accept", (_, _) => delayed.Task);
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        var pbi = new ProductBacklogItem
        { ProjectId = 17, Title = "Proposal", ProblemStatementId = 9, GenerationRunId = 7, GenerationCandidateKey = "story-1" };
        var callbacks = 0;
        using var cut = Render<GeneratedPbiDialog>(p => p.Add(x => x.CapturedWork, scope)
            .Add(x => x.Open, true).Add(x => x.ProjectId, 17).Add(x => x.PBIs, [pbi])
            .Add(x => x.Proposals, [new(1, 9, 7, pbi, new(true, true, false, false, false, false, null))])
            .Add(x => x.OnSaved, _ => callbacks++));
        var pending = cut.FindAll("fluent-button").Single(x => x.TextContent.Trim() == "Add to Backlog").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Contains(Http.Requests, request => request.Method == HttpMethod.Post));
        scope.Dispose();
        delayed.SetResult(MockHttpHandler.Json(new[] { pbi }));
        await pending;
        Assert.Equal(0, callbacks);
        Assert.DoesNotContain("All generated PBIs have been saved", cut.Markup);
    }

    [Fact]
    public async Task Graph_callbacks_reject_unknown_render_tokens_and_retired_tenants()
    {
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        var pbi = new ProductBacklogItem { PbiId = 5, ProjectId = 17, Title = "Current tenant task", Type = PbiType.Task };
        var graph = new DependencyGraphDto(17, 4, 9,
            [new(pbi, "Ready", false, true, false, false, [], [])], [], "", []);
        using var cut = Render<SprintDependencyView>(p => p.Add(x => x.CapturedWork, scope).Add(x => x.Graph, graph));
        Assert.DoesNotContain("Unapproved / legacy scope", cut.Markup);
        Assert.DoesNotContain("approved file scope", cut.Markup);
        await cut.InvokeAsync(() => cut.Instance.OpenNodeAsync(5, 999));
        Assert.False(cut.FindComponent<PbiDetailsDialog>().Instance.Open);
        scope.Dispose();
        await cut.InvokeAsync(() => cut.Instance.OpenNodeAsync(5, 0));
        Assert.False(cut.FindComponent<PbiDetailsDialog>().Instance.Open);
        Assert.Empty(Http.Requests);
    }

    [Fact]
    public async Task Dependency_edit_posts_captured_tenant_project_and_graph_revision()
    {
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        Http.OnStatus(HttpMethod.Put, "api/organizations/2/projects/17/dependencies", HttpStatusCode.OK);
        var graph = new DependencyGraphDto(17, 4, 9,
            [new(new ProductBacklogItem { PbiId = 5, ProjectId = 17, Title = "Task", Type = PbiType.Task },
                "Ready", false, true, false, false, [], [])], [], "", []);
        var changed = 0;
        using var cut = Render<SprintDependencyView>(p => p.Add(x => x.CapturedWork, scope).Add(x => x.Graph, graph)
            .Add(x => x.OnChanged, () => changed++));
        cut.FindAll("fluent-button").Single(x => x.TextContent.Trim() == "Dependencies").Click();
        cut.Find("section[aria-label='Edit dependencies'] input").Change("6, 8");
        await cut.FindAll("fluent-button").Single(x => x.TextContent.Trim() == "Save dependencies").ClickAsync(new());
        var request = Assert.Single(Http.Requests);
        Assert.Equal("api/organizations/2/projects/17/dependencies", request.Path);
        var payload = JsonSerializer.Deserialize<SetDependenciesRequest>(request.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(5, payload.DependentId);
        Assert.Equal(9, payload.ExpectedRevision);
        Assert.Equal([6, 8], payload.PrerequisiteIds);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Pbi_editor_preserves_persisted_provenance_parent_and_version_in_saves()
    {
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        var original = new ProductBacklogItem
        {
            PbiId = 5,
            ProjectId = 17,
            Title = "Task",
            Type = PbiType.Task,
            ParentStoryId = 3,
            ProblemStatementId = 9,
            GenerationRunId = 7,
            GenerationCandidateKey = "task-1",
            Version = 11
        };
        Http.On(HttpMethod.Put, "api/organizations/2/Pbi", (_, _) => MockHttpHandler.Json(original));
        using var cut = Render<PbiDetailsDialog>(p => p.Add(x => x.CapturedWork, scope)
            .Add(x => x.ProjectId, 17).Add(x => x.Pbi, original).Add(x => x.Open, true));
        await cut.Find("form").SubmitAsync();
        var request = Assert.Single(Http.Requests, x => x.Method == HttpMethod.Put);
        var payload = JsonSerializer.Deserialize<ProductBacklogItem>(request.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(original.ProblemStatementId, payload.ProblemStatementId);
        Assert.Equal(original.GenerationRunId, payload.GenerationRunId);
        Assert.Equal(original.GenerationCandidateKey, payload.GenerationCandidateKey);
        Assert.Equal(original.ParentStoryId, payload.ParentStoryId);
        Assert.Equal(original.Version, payload.Version);
    }

    [Fact]
    public async Task Task_specification_exports_use_captured_tenant_and_stop_after_scope_retirement()
    {
        using var client = Http.CreateClient();
        using var scope = new WorkScope(client, 2, 17);
        const string root = "api/organizations/2/projects/17/tasks/5";
        Http.OnGetJson(root + "/specifications", new TaskSpecificationStatusDto(null, false, []));
        Http.OnGetJson(root + "/specifications/history", Array.Empty<TaskSpecificationRevision>());
        Http.OnGetJson(root + "/pull-requests", Array.Empty<object>());
        Http.On(HttpMethod.Get, root + "/specifications/export", (_, _) => new(HttpStatusCode.OK) { Content = new StringContent("Scoped handoff") });
        using var cut = Render<TaskSpecificationEditor>(p => p.Add(x => x.CapturedWork, scope)
            .Add(x => x.ProjectId, 17).Add(x => x.TaskId, 5));
        await cut.FindAll("fluent-button").Single(x => x.TextContent.Trim() == "Export Markdown handoff").ClickAsync(new());
        var invocation = Assert.Single(JSInterop.Invocations, x => x.Identifier == "mermaidHelper.downloadText");
        Assert.Equal("task-5-specification.md", invocation.Arguments[0]);
        Assert.Equal("Scoped handoff", invocation.Arguments[1]);
        scope.Dispose();
        await cut.FindAll("fluent-button").Single(x => x.TextContent.Trim() == "Export Markdown handoff").ClickAsync(new());
        Assert.Single(JSInterop.Invocations, x => x.Identifier == "mermaidHelper.downloadText");
        Assert.All(Http.Requests, request => Assert.StartsWith(root, request.Path));
    }

    [Fact]
    public void Child_scope_preserves_gateway_base_and_parent_cancellation_without_canceling_siblings()
    {
        using var client = new HttpClient { BaseAddress = new Uri("https://gateway/fluentweb/_api/webapi/") };
        using var parent = new WorkScope(client, 2, 17);
        using var first = parent.CreateChild();
        using var second = parent.CreateChild();
        Assert.Equal("https://gateway/fluentweb/_api/webapi/hubs/organizations/2/project-work",
            new Uri(first.Http.BaseAddress!, OrganizationRoutes.ProjectWorkHub(first.OrganizationId)).ToString());
        first.Dispose();
        Assert.True(parent.IsCurrent);
        Assert.True(second.IsCurrent);
        parent.Dispose();
        Assert.False(second.IsCurrent);
    }
}
