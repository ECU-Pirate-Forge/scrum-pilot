using Xunit;
using AngleSharp.Dom;
using Bunit;

namespace ScrumPilot.FluentWeb.Tests.Smoke;

public class AppSmokeTests : FluentFrontendTestBase
{
    [Fact]
    public void App_renders_home_page_and_has_no_mud_classes()
    {
        using var cut = Render<ScrumPilot.FluentWeb.App>();

        var heading = cut.Find("h1");
        Assert.Contains("ScrumPilot", heading.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Single(cut.FindAll("h1"));
        Assert.DoesNotContain("mud-", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
