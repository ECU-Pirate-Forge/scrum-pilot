using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.FluentWeb.Components.DependencyChart;
using ScrumPilot.FluentWeb.Components.ScrumBoard;
using ScrumPilot.FluentWeb.Pages;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.FluentWeb.Tests.TestHelpers;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.ScrumBoard;

public class ScrumBoardPageTests : FluentFrontendTestBase
{
    public ScrumBoardPageTests()
    {
        Services.GetRequiredService<ProjectStateService>().SetProject(new Project
        {
            ProjectId = 42,
            OrganizationId = 7,
            ProjectName = "Migration"
        });
    }

    [Fact]
    public void Board_renders_lanes_and_cards()
    {
        RegisterBoardData(SampleItems());

        using var cut = Render<ScrumPilot.FluentWeb.Pages.ScrumBoard>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("To Do", cut.Markup);
            Assert.Contains("In Progress", cut.Markup);
            Assert.Contains("In Review", cut.Markup);
            Assert.Contains("Done", cut.Markup);
            Assert.Contains("Implement board", cut.Markup);
            Assert.Contains("Review API", cut.Markup);
            Assert.Contains("5 SP", cut.Markup);
            Assert.Contains("Flagged", cut.Markup);
            Assert.DoesNotContain("m" + "ud-", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Move_to_menu_sends_expected_request_and_updates_lane()
    {
        RegisterBoardData(SampleItems());
        Http.On(HttpMethod.Put, "api/Pbi", (_, body) =>
        {
            var updated = JsonContentToPbi(body!);
            return MockHttpHandler.Json(updated);
        });

        using var cut = Render<ScrumPilot.FluentWeb.Pages.ScrumBoard>();
        cut.WaitForAssertion(() => Assert.Contains("Implement board", cut.Markup));

        var card = cut.FindComponents<BoardCard>().First(c => c.Instance.Item.PbiId == 1);
        card.InvokeAsync(() => card.Instance.RequestMoveAsync(PbiStatus.InProgress));

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Put, "api/Pbi"));
            var sent = JsonContentToPbi(request.Body!);
            Assert.Equal(1, sent.PbiId);
            Assert.Equal(PbiStatus.InProgress, sent.Status);
            Assert.Contains("In Progress", cut.FindComponents<BoardLane>().First(l => l.Instance.Lane.Status == PbiStatus.InProgress).Markup);
        });
    }

    [Fact]
    public void Failed_move_rolls_back_to_original_lane()
    {
        RegisterBoardData(SampleItems());
        Http.OnStatus(HttpMethod.Put, "api/Pbi", HttpStatusCode.InternalServerError);

        using var cut = Render<ScrumPilot.FluentWeb.Pages.ScrumBoard>();
        cut.WaitForAssertion(() => Assert.Contains("Implement board", cut.Markup));

        var card = cut.FindComponents<BoardCard>().First(c => c.Instance.Item.PbiId == 1);
        card.Find("article").DragStart(new DragEventArgs());
        var doneLane = cut.FindComponents<BoardLane>().First(l => l.Instance.Lane.Status == PbiStatus.Done);
        doneLane.Find("section").Drop(new DragEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Single(Http.RequestsTo(HttpMethod.Put, "api/Pbi"));
            Assert.Contains("Could not save the status change", cut.Markup);
            Assert.Contains("Implement board", cut.FindComponents<BoardLane>().First(l => l.Instance.Lane.Status == PbiStatus.ToDo).Markup);
            Assert.DoesNotContain("Implement board", cut.FindComponents<BoardLane>().First(l => l.Instance.Lane.Status == PbiStatus.Done).Markup);
        });
    }

    private void RegisterBoardData(List<ProductBacklogItem> items)
    {
        Http.OnGetJson("api/Sprint", new[]
        {
            new Sprint
            {
                SprintId = 10,
                ProjectId = 42,
                SprintTitle = "Sprint 10",
                IsOpen = true,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(5)
            }
        });
        Http.OnGetJson("api/Epic", new[] { new Epic { EpicId = 3, ProjectId = 42, Name = "UI" } });
        Http.OnGetJson("api/user/all", new[] { new UserSummaryDto { Id = "u1", UserName = "Ada Lovelace" } });
        Http.OnGetJson("api/Pbi/getNonDraftPbis", items);
    }

    private static ProductBacklogItem JsonContentToPbi(string body) =>
        System.Text.Json.JsonSerializer.Deserialize<ProductBacklogItem>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

    private static List<ProductBacklogItem> SampleItems() =>
    [
        new()
        {
            PbiId = 1,
            ProjectId = 42,
            Title = "Implement board",
            Type = PbiType.Story,
            Status = PbiStatus.ToDo,
            Priority = PbiPriority.High,
            StoryPoints = PbiPoints.Five,
            AssignedToUserId = "u1",
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        },
        new()
        {
            PbiId = 2,
            ProjectId = 42,
            Title = "Review API",
            Type = PbiType.Task,
            Status = PbiStatus.InReview,
            Priority = PbiPriority.Medium,
            StoryPoints = PbiPoints.Two,
            IsFlagged = true,
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        }
    ];
}

public class DependencyChartPageTests : FluentFrontendTestBase
{
    public DependencyChartPageTests()
    {
        Services.GetRequiredService<ProjectStateService>().SetProject(new Project
        {
            ProjectId = 42,
            OrganizationId = 7,
            ProjectName = "Migration"
        });
    }

    [Fact]
    public void Dependency_chart_page_passes_sprintId()
    {
        var graph = new DependencyGraphDto(42, 10, 7, SampleItems().Select(item => new DependencyGraphNodeDto(
            item, item.Status == PbiStatus.Done ? "Done" : "Active", item.Status == PbiStatus.Done,
            false, false, false, [], [])).ToArray(), [new(2, 1)], "flowchart LR\nPBI1 --> PBI2", []);
        Http.OnGetJson("api/projects/42/sprints/10/dependency-graph", graph);
        Http.OnGetJson("api/Sprint", new[] { new Sprint { SprintId = 10, ProjectId = 42, SprintTitle = "Sprint 10" } });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/dependency-chart?sprintId=10");

        using var cut = Render<DependencyChartPage>();

        cut.WaitForAssertion(() =>
        {
            var chart = cut.FindComponent<SprintDependencyView>();
            Assert.Equal(10, chart.Instance.Graph.SprintId);
            Assert.Equal(7, chart.Instance.Graph.Revision);
            Assert.Contains("Foundation", cut.Markup);
            Assert.Contains("Dependent work", cut.Markup);
        });
    }

    [Fact]
    public void MissingSprintRequiresExplicitSelection()
    {
        using var cut = Render<DependencyChartPage>();
        cut.WaitForAssertion(() => Assert.Contains("Choose a sprint", cut.Markup));
        Assert.Empty(cut.FindComponents<SprintDependencyView>());
    }

    [Fact]
    public async Task LiveRefreshDoesNotReplaceTheDependencyEditorsExpectedRevision()
    {
        var graph = new DependencyGraphDto(42, 10, 7, SampleItems().Select(item => new DependencyGraphNodeDto(
            item, "Active", false, false, false, false, [], [])).ToArray(), [new(2, 1)], "", []);
        Http.On(HttpMethod.Put, "api/projects/42/dependencies", (_, _) =>
            new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("Reload the changed graph.") });
        using var cut = Render<SprintDependencyView>(parameters => parameters.Add(component => component.Graph, graph));
        cut.FindAll("fluent-button").Last(button => button.TextContent.Trim() == "Dependencies").Click();
        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.Graph,
            graph with { Revision = 8, Edges = [new(2, 1), new(2, 3)] })));
        cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Save dependencies").Click();
        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Put, "api/projects/42/dependencies"));
            var body = JsonSerializer.Deserialize<SetDependenciesRequest>(request.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(7, body.ExpectedRevision);
            Assert.Contains("Reload the changed graph", cut.Markup);
        });
    }

    [Fact]
    public void ReadyFilterRetainsPrerequisiteContextInDiagramButNotFilteredTable()
    {
        var ready = new ProductBacklogItem { PbiId = 1, ProjectId = 42, Title = "Ready work" };
        var blocked = new ProductBacklogItem { PbiId = 2, ProjectId = 42, Title = "Blocked work" };
        var done = new ProductBacklogItem { PbiId = 3, ProjectId = 42, Title = "Completed prerequisite", Status = PbiStatus.Done };
        var graph = new DependencyGraphDto(42, 10, 1,
            [new(ready, "Ready", false, true, true, false, [], []),
             new(blocked, "Blocked", false, false, false, false, [4], []),
             new(done, "Done", true, false, false, false, [], [])], [new(1, 3)], "", []);
        using var cut = Render<SprintDependencyView>(parameters => parameters.Add(component => component.Graph, graph));
        cut.Find("select").Change("Ready");
        cut.WaitForAssertion(() =>
        {
            var definition = (string)JSInterop.Invocations["mermaidHelper.render"].Last().Arguments[1]!;
            Assert.Contains("Ready work", definition);
            Assert.Contains("Completed prerequisite", definition);
            Assert.DoesNotContain("Blocked work", definition);
            Assert.DoesNotContain("Blocked work", cut.Find("tbody").TextContent);
        });
    }

    private static List<ProductBacklogItem> SampleItems() =>
    [
        new()
        {
            PbiId = 1,
            ProjectId = 42,
            Title = "Foundation",
            Status = PbiStatus.Done,
            Priority = PbiPriority.Medium,
            StoryPoints = PbiPoints.Three,
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        },
        new()
        {
            PbiId = 2,
            ProjectId = 42,
            Title = "Dependent work",
            Status = PbiStatus.InProgress,
            Priority = PbiPriority.High,
            StoryPoints = PbiPoints.Five,
            DependsOnPbiId = 1,
            IsFlagged = true,
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        }
    ];
}








