using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using BacklogPage = ScrumPilot.FluentWeb.Pages.Backlog;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.FluentWeb.Tests.TestHelpers;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Backlog;

public class BacklogPageTests : FluentFrontendTestBase
{
    public BacklogPageTests()
    {
        Services.GetRequiredService<ProjectStateService>().SetProject(new Project
        {
            ProjectId = 7,
            OrganizationId = 1,
            ProjectName = "Migration project"
        });
    }

    [Fact]
    public void Backlog_loads_and_renders_items()
    {
        RegisterBacklogRoutes(SamplePbis(), SampleSprints());

        using var cut = Render<BacklogPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Backlog", cut.Markup);
            Assert.Contains("Login keeps failing", cut.Markup);
            Assert.Contains("Write onboarding story", cut.Markup);
            Assert.Contains("2 PBIs", cut.Markup);
            Assert.DoesNotContain("m" + "ud-", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Search_filter_narrows_backlog_list()
    {
        RegisterBacklogRoutes(SamplePbis(), SampleSprints());
        using var cut = Render<BacklogPage>();
        cut.WaitForAssertion(() => Assert.Contains("Login keeps failing", cut.Markup));

        cut.Find("#backlog-search").Input("onboarding");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Write onboarding story", cut.Markup);
            Assert.DoesNotContain("Login keeps failing", cut.Markup);
            Assert.Contains("1 PBIs", cut.Markup);
        });
    }

    [Fact]
    public void Assign_to_sprint_menu_sends_expected_api_request()
    {
        RegisterBacklogRoutes(SamplePbis(), SampleSprints());
        Http.On(HttpMethod.Put, "api/Pbi", (req, body) =>
        {
            var pbi = DeserializePbi(body);
            Assert.Equal(10, pbi.SprintId);
            return MockHttpHandler.Json(pbi);
        });

        using var cut = Render<BacklogPage>();
        cut.WaitForAssertion(() => Assert.Contains("Login keeps failing", cut.Markup));

        cut.Find("[data-testid='assign-sprint-10-pbi-1']").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Put, "api/Pbi"));
            var sent = DeserializePbi(request.Body);
            Assert.Equal(1, sent.PbiId);
            Assert.Equal(10, sent.SprintId);
        });
    }

    [Fact]
    public void Failed_move_shows_error_and_rolls_back()
    {
        RegisterBacklogRoutes(SamplePbis(), SampleSprints());
        Http.OnStatus(HttpMethod.Put, "api/Pbi", HttpStatusCode.InternalServerError);

        using var cut = Render<BacklogPage>();
        cut.WaitForAssertion(() => Assert.Contains("Login keeps failing", cut.Markup));

        cut.Find("#backlog-target-sprint").Change("10");
        cut.Find("[data-testid='assign-sprint-10-pbi-1']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Failed to update the PBI sprint. Your change was rolled back.", cut.Markup);
            Assert.Contains("No stories assigned yet", cut.Markup);
        });
    }

    private void RegisterBacklogRoutes(List<ProductBacklogItem> pbis, List<Sprint> sprints)
    {
        Http.OnGetJson("api/Sprint", sprints);
        Http.OnGetJson("api/Pbi/getNonDraftPbis", pbis);
        Http.OnGetJson("api/metrics/velocity", Array.Empty<VelocityPoint>());
    }

    private static List<Sprint> SampleSprints() =>
    [
        new()
        {
            SprintId = 10,
            ProjectId = 7,
            SprintTitle = "Sprint 10",
            SprintGoal = "Stabilize login",
            StartDate = new DateTime(2026, 10, 1),
            EndDate = new DateTime(2026, 10, 14),
            IsOpen = true
        }
    ];

    private static List<ProductBacklogItem> SamplePbis() =>
    [
        new()
        {
            PbiId = 1,
            ProjectId = 7,
            Title = "Login keeps failing",
            Description = "Bug in auth redirect",
            Type = PbiType.Bug,
            Status = PbiStatus.ToDo,
            Priority = PbiPriority.High,
            StoryPoints = PbiPoints.Three,
            Origin = PbiOrigin.WebUserCreated,
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        },
        new()
        {
            PbiId = 2,
            ProjectId = 7,
            Title = "Write onboarding story",
            Description = "Document the new user flow",
            Type = PbiType.Story,
            Status = PbiStatus.InProgress,
            Priority = PbiPriority.Low,
            StoryPoints = PbiPoints.Five,
            Origin = PbiOrigin.WebUserCreated,
            DateCreated = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        }
    ];

    private static ProductBacklogItem DeserializePbi(string? body) =>
        JsonSerializer.Deserialize<ProductBacklogItem>(body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}





