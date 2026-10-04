using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.FluentWeb.Pages;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Pbi;

public class DraftPbiPageTests : FluentFrontendTestBase
{
    [Fact]
    public void DraftPbiPage_loads_drafts_for_selected_project()
    {
        Services.GetRequiredService<ProjectStateService>().SetProject(new Project { ProjectId = 7, ProjectName = "Pilot" });
        Http.OnGetJson("api/Pbi/getDraftPbis", new[] { SampleDraft(101, "Draft checkout") });

        using var cut = Render<DraftPbiPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Draft checkout", cut.Markup);
            Assert.Single(Http.RequestsTo(HttpMethod.Get, "api/Pbi/getDraftPbis"));
        });
    }

    [Fact]
    public void DraftPbiPage_prompts_for_project_when_none_selected()
    {
        using var cut = Render<DraftPbiPage>();

        Assert.Contains("Select a project", cut.Markup);
        Assert.Empty(Http.Requests);
    }

    private static ProductBacklogItem SampleDraft(int id, string title) => new()
    {
        PbiId = id,
        ProjectId = 7,
        Title = title,
        Description = "Generated draft",
        Type = PbiType.Story,
        Status = PbiStatus.ToDo,
        Priority = PbiPriority.Medium,
        StoryPoints = PbiPoints.Three,
        IsDraft = true,
        DateCreated = DateTime.UtcNow,
        LastUpdated = DateTime.UtcNow
    };
}
