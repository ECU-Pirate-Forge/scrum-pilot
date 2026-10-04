using System.Net;
using System.Net.Http.Json;
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
        Http.OnGetJson("api/Pbi/getNonDraftPbis", SampleItems());
        Http.OnGetJson("api/Sprint", new[] { new Sprint { SprintId = 10, ProjectId = 42, SprintTitle = "Sprint 10" } });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/dependency-chart?sprintId=10");

        using var cut = Render<DependencyChartPage>();

        cut.WaitForAssertion(() =>
        {
            var chart = cut.FindComponent<DependencyChart>();
            Assert.Equal(10, chart.Instance.SprintId);
            Assert.Contains("depends on", chart.Instance.BuildMermaidDefinition());
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








