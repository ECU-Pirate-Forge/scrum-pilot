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
            // Base class handles MudServices and JSInterop setup
        }

        [Fact]
        public void HomePage_RendersCorrectly()
        {
            // Act
            var component = Render<Home>();

            // Assert
            Assert.NotNull(component);
            Assert.Contains("Scrum Pilot Dashboard", component.Markup);
            Assert.Contains("Quick access to core Scrum Pilot workflows.", component.Markup);
        }

        [Fact]
        public void HomePage_ContainsDashboardTiles()
        {
            // Act
            var component = Render<Home>();

            // Assert
            var dashboardTiles = component.FindComponents<Web.Components.DashboardTile>();
            Assert.Equal(4, dashboardTiles.Count);
        }

        [Fact]
        public void HomePage_HasCorrectTileContent()
        {
            // Act
            var component = Render<Home>();

            // Assert
            Assert.Contains("Scrum Board", component.Markup);
            Assert.Contains("Generate PBIs", component.Markup);
            Assert.Contains("Draft PBIs", component.Markup);
            Assert.Contains("Backlog", component.Markup);
        }

        [Fact]
        public void HomePage_HasCorrectTileDescriptions()
        {
            // Act
            var component = Render<Home>();

            // Assert
            Assert.Contains("View and manage sprint work.", component.Markup);
            Assert.Contains("Create product backlog items quickly.", component.Markup);
            Assert.Contains("Review and refine drafted product backlog items.", component.Markup);
        }

        [Fact]
        public void HomePage_UsesCorrectContainerLayout()
        {
            // Act
            var component = Render<Home>();

            // Assert
            Assert.Contains("mud-container", component.Markup);
            Assert.Contains("mud-grid", component.Markup);
        }
    }
}

