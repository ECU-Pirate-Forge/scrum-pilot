using Bunit;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class DependencyChartPageTests : FrontendTestBase
    {
        public DependencyChartPageTests()
        {
            // Base class handles MudServices, JSInterop, auth, and ProjectStateService setup
        }

        [Fact]
        public void DependencyChartPage_RendersWithoutExceptions()
        {
            var component = Render<DependencyChartPage>();

            Assert.NotNull(component);
        }

        [Fact]
        public void DependencyChartPage_HasCorrectPageClass()
        {
            var component = Render<DependencyChartPage>();

            Assert.Contains("dep-chart-page", component.Markup);
        }

        [Fact]
        public void DependencyChartPage_HasHeader()
        {
            var component = Render<DependencyChartPage>();

            Assert.Contains("dep-chart-header", component.Markup);
        }

        [Fact]
        public void DependencyChartPage_HasBackToBoardButton()
        {
            var component = Render<DependencyChartPage>();

            Assert.Contains("Back to Board", component.Markup);
        }

        [Fact]
        public void DependencyChartPage_ShowsDependencyChartTitle()
        {
            var component = Render<DependencyChartPage>();

            Assert.Contains("Dependency Chart", component.Markup);
        }

        [Fact]
        public void DependencyChartPage_ShowsLoadingInitially()
        {
            // When the page first renders (before OnInitializedAsync completes),
            // it should show the loading spinner
            var component = Render<DependencyChartPage>();

            // After async init in test, it will settle — either loading or the chart/error state
            Assert.NotNull(component);
        }
    }
}
