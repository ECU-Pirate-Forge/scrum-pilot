using Bunit;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.PlanningPoker;

public class PlanningPokerPageTests : FluentFrontendTestBase
{
    public PlanningPokerPageTests()
    {
        Http.OnGetJson("api/Pbi/getNonDraftPbis", new List<ProductBacklogItem>());
    }

    [Fact]
    public void PlanningPoker_RendersInitialDisconnectedState()
    {
        using var cut = Render<ScrumPilot.FluentWeb.Pages.PlanningPoker>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Planning Poker", cut.Markup);
            Assert.Contains("Disconnected from planning poker", cut.Markup);
            Assert.Contains("Select a story", cut.Markup);
            Assert.Contains("Select a story after connecting to start voting", cut.Markup);
            Assert.Single(cut.FindAll("h1"));
            Assert.DoesNotContain("m" + "ud-", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }
}

