using ApexCharts;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.FluentWeb.Components.MetricsDashboard;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;
using Xunit;
using MetricsPage = ScrumPilot.FluentWeb.Pages.MetricsDashboard;

namespace ScrumPilot.FluentWeb.Tests.Metrics;

public class MetricsDashboardTests : FluentFrontendTestBase
{
    [Fact]
    public void Dashboard_renders_widget_cards_for_configured_widgets()
    {
        ArrangeProjectAndMetricEndpoints();

        using var cut = Render<MetricsPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Metrics Dashboard", cut.Markup);
            Assert.Contains("Days Left in Sprint", cut.Markup);
            Assert.Contains("Committed Points", cut.Markup);
            Assert.Contains("Remaining Story Points", cut.Markup);
            Assert.Contains("Sprint Progress", cut.Markup);
            Assert.Contains("Burndown Chart", cut.Markup);
            Assert.Contains("Work In Progress", cut.Markup);
            Assert.Contains("Sprint Velocity", cut.Markup);
            Assert.Contains("Status Distribution", cut.Markup);
            Assert.Contains("PBI by Type", cut.Markup);
            Assert.DoesNotContain("mud-", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Widget_shows_empty_state_when_no_data()
    {
        using var cut = Render<StatusDistributionWidget>(parameters => parameters.Add(p => p.Items, []));

        Assert.Contains("No data.", cut.Markup);
        Assert.DoesNotContain("mud-", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Theme_mapping_uses_matching_apex_chart_modes()
    {
        Assert.Equal(Mode.Dark, MetricsChartTheme.GetApexMode(true));
        Assert.Equal(Mode.Light, MetricsChartTheme.GetApexMode(false));
        Assert.Equal(Mode.Dark, MetricsChartTheme.Tooltip(true).Theme);
        Assert.Equal(Mode.Light, MetricsChartTheme.Theme(false).Mode);
    }

    private void ArrangeProjectAndMetricEndpoints()
    {
        var project = new Project { ProjectId = 7, ProjectName = "Test project" };
        Services.GetRequiredService<ProjectStateService>().SetProject(project);

        var sprint = new Sprint
        {
            SprintId = 3,
            ProjectId = project.ProjectId,
            SprintGoal = "Ship metrics",
            StartDate = DateTime.Today.AddDays(-3),
            EndDate = DateTime.Today.AddDays(7),
            IsOpen = true
        };

        var pbis = new List<ProductBacklogItem>
        {
            new() { PbiId = 1, ProjectId = project.ProjectId, SprintId = sprint.SprintId, Title = "Build chart", Type = PbiType.Story, Status = PbiStatus.InProgress, Priority = PbiPriority.High },
            new() { PbiId = 2, ProjectId = project.ProjectId, SprintId = sprint.SprintId, Title = "Fix data", Type = PbiType.Bug, Status = PbiStatus.Done, Priority = PbiPriority.Medium }
        };

        Http.OnGetJson("api/sprint", new List<Sprint> { sprint });
        Http.OnGetJson("api/pbi/getNonDraftPbis", pbis);
        Http.OnGetJson("api/dashboard-preferences", new DashboardPreferenceDto());
        Http.OnGetJson("api/metrics/sprint-progress/3", new SprintProgressDto(13, 5, 2, 1));
        Http.OnGetJson("api/metrics/burndown/3", new List<BurndownPoint> { new(DateTime.Today, 8, 13), new(DateTime.Today.AddDays(1), 6, 10) });
        Http.OnGetJson("api/metrics/velocity", new List<VelocityPoint> { new("Sprint 1", 8, 7), new("Sprint 2", 10, 9), new("Sprint 3", 13, 5) });
        Http.OnGetJson("api/metrics/wip/3", new List<WipItem> { new(1, "Build chart", "Story", "High", "In Progress") });
        Http.OnGetJson("api/metrics/bug-trend/3", new List<BugTrendPoint> { new(DateTime.Today, 1, 0), new(DateTime.Today.AddDays(1), 0, 1) });
        Http.OnGetJson("api/metrics/cycle-time/3", new List<CycleTimePoint> { new(DateTime.Today, 2.5) });
        Http.OnGetJson("api/metrics/work-by-status/3", new List<WorkByStatusPoint> { new("In Progress", 5, 0), new("Done", 0, 3) });
        Http.OnGetJson("api/metrics/time-in-stage/3", new TimeInStageData { Stages = ["InProgress", "InReview"], Points = [new("≤1d", "InProgress", 1)] });
    }
}
