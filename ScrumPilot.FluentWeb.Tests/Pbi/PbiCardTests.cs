using Bunit;
using ScrumPilot.FluentWeb.Components.Pbi;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Pbi;

public class PbiCardTests : FluentFrontendTestBase
{
    [Fact]
    public void PbiCard_renders_summary_fields()
    {
        var pbi = SamplePbi();
        using var cut = Render<PbiCard>(parameters => parameters
            .Add(p => p.PbiModel, pbi)
            .Add(p => p.ProjectId, 42));

        Assert.Contains("Checkout fails", cut.Markup);
        Assert.Contains("Fix checkout payment failure", cut.Markup);
        Assert.Contains("Bug", cut.Markup);
        Assert.Contains("High", cut.Markup);
        Assert.Contains("Flagged", cut.Markup);
        Assert.DoesNotContain("mud-", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PbiCard_opens_details_editor_from_public_method()
    {
        Http.OnGetJson("api/Epic", Array.Empty<Epic>());
        Http.OnGetJson("api/Sprint", Array.Empty<Sprint>());
        Http.OnGetJson("api/user/all", Array.Empty<UserSummaryDto>());
        Http.OnGetJson("api/Pbi/getNonDraftPbis", Array.Empty<ProductBacklogItem>());
        Http.OnGetJson("api/comments/pbi/12", Array.Empty<Comment>());

        using var cut = Render<PbiCard>(parameters => parameters
            .Add(p => p.PbiModel, SamplePbi())
            .Add(p => p.ProjectId, 42));

        cut.FindAll("button, fluent-button").First(e => e.TextContent.Contains("Edit", StringComparison.OrdinalIgnoreCase)).Click();

        cut.WaitForAssertion(() => Assert.Contains("PBI details", cut.Markup));
    }

    private static ProductBacklogItem SamplePbi() => new()
    {
        PbiId = 12,
        ProjectId = 42,
        Title = "Checkout fails",
        Description = "Fix checkout payment failure",
        Type = PbiType.Bug,
        Status = PbiStatus.InProgress,
        Priority = PbiPriority.High,
        StoryPoints = PbiPoints.Five,
        IsFlagged = true,
        DateCreated = DateTime.UtcNow,
        LastUpdated = DateTime.UtcNow
    };
}
