using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class HomePageTests : FrontendTestBase
    {
        public HomePageTests()
        {
            // Base class handles MudServices, JSInterop, auth, and ProjectStateService setup
        }

        [Fact]
        public void HomePage_RendersCorrectly()
        {
            var component = Render<Home>();

            Assert.NotNull(component);
            // Page renders the personalised greeting and the tile grid
            Assert.Contains("Welcome back to ScrumPilot", component.Markup);
            Assert.Contains("mud-grid", component.Markup);
        }

        [Fact]
        public void HomePage_ContainsDashboardTiles()
        {
            var component = Render<Home>();

            // Home now has 8 tiles
            var dashboardTiles = component.FindComponents<Web.Components.DashboardTile>();
            Assert.Equal(8, dashboardTiles.Count);
        }

        [Fact]
        public void HomePage_HasCorrectTileContent()
        {
            var component = Render<Home>();

            Assert.Contains("Scrum Board", component.Markup);
            Assert.Contains("Generate PBIs", component.Markup);
            Assert.Contains("Draft PBIs", component.Markup);
            Assert.Contains("Backlog", component.Markup);
            Assert.Contains("Metrics Dashboard", component.Markup);
            Assert.Contains("Planning Poker", component.Markup);
            Assert.Contains("Manage Project", component.Markup);
            Assert.Contains("User Settings", component.Markup);
        }

        [Fact]
        public void HomePage_HasCorrectTileDescriptions()
        {
            var component = Render<Home>();

            Assert.Contains("View and manage sprint work.", component.Markup);
            Assert.Contains("Create product backlog items quickly.", component.Markup);
            Assert.Contains("Review and refine drafted product backlog items.", component.Markup);
            Assert.Contains("View and manage backlog items.", component.Markup);
            Assert.Contains("Sprint metrics and PBI analytics.", component.Markup);
        }

        [Fact]
        public void HomePage_UsesCorrectContainerLayout()
        {
            var component = Render<Home>();

            Assert.Contains("mud-container", component.Markup);
            Assert.Contains("mud-grid", component.Markup);
        }
    }
}

