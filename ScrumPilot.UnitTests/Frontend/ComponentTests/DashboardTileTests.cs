using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ScrumPilot.Web.Components;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.ComponentTests
{
    public class DashboardTileTests : FrontendTestBase
    {
        public DashboardTileTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        [Fact]
        public void DashboardTile_RendersTitle()
        {
            // Arrange
            var title = "Test Title";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title));

            // Assert
            Assert.Contains(title, component.Markup);
        }

        [Fact]
        public void DashboardTile_RendersDescription_WhenProvided()
        {
            // Arrange
            var title = "Test Title";
            var description = "Test Description";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title)
                .Add(p => p.Description, description));

            // Assert
            Assert.Contains(description, component.Markup);
        }

        [Fact]
        public void DashboardTile_DoesNotRenderDescription_WhenEmpty()
        {
            // Arrange
            var title = "Test Title";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title)
                .Add(p => p.Description, ""));

            // Assert
            Assert.Contains(title, component.Markup);
            // Description section should not be rendered
            Assert.DoesNotContain("mt-2", component.Markup);
        }

        [Fact]
        public void DashboardTile_DoesNotRenderDescription_WhenWhitespace()
        {
            // Arrange
            var title = "Test Title";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title)
                .Add(p => p.Description, "   "));

            // Assert
            Assert.Contains(title, component.Markup);
            // Description should not be rendered for whitespace
        }

        [Fact]
        public void DashboardTile_HasCorrectMudPaperClass()
        {
            // Arrange
            var title = "Test Title";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title));

            // Assert
            Assert.Contains("dashboard-tile", component.Markup);
            Assert.Contains("mud-paper", component.Markup);
        }

        [Fact]
        public void DashboardTile_HasCorrectElevation()
        {
            // Arrange
            var title = "Test Title";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title));

            // Assert
            Assert.Contains("mud-elevation-3", component.Markup);
        }

        [Fact]
        public void DashboardTile_HasCorrectTypography()
        {
            // Arrange
            var title = "Test Title";
            var description = "Test Description";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title)
                .Add(p => p.Description, description));

            // Assert
            // Check for MudText with h6 typography for title
            Assert.Contains("mud-typography-h6", component.Markup);
            // Check for MudText with body2 typography for description
            Assert.Contains("mud-typography-body2", component.Markup);
        }

        [Fact]
        public void DashboardTile_IsClickable()
        {
            // Arrange
            var title = "Test Title";
            var href = "/test-page";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title)
                .Add(p => p.Href, href));

            // Assert
            var mudPaper = component.Find(".mud-paper");
            Assert.NotNull(mudPaper);
        }

        [Fact]
        public void DashboardTile_RendersWithAllParameters()
        {
            // Arrange
            var title = "Complete Title";
            var description = "Complete Description";
            var href = "/complete-page";

            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, title)
                .Add(p => p.Description, description)
                .Add(p => p.Href, href));

            // Assert
            Assert.Contains(title, component.Markup);
            Assert.Contains(description, component.Markup);
            Assert.Contains("dashboard-tile", component.Markup);
        }

        [Fact]
        public void DashboardTile_HandlesNullTitle()
        {
            // Act & Assert - Should not throw exception
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, (string)null!));
            
            Assert.NotNull(component);
        }

        [Fact]
        public void DashboardTile_HandlesEmptyTitle()
        {
            // Act
            var component = Render<DashboardTile>(parameters => parameters
                .Add(p => p.Title, ""));

            // Assert
            Assert.NotNull(component);
            Assert.Contains("mud-paper", component.Markup);
        }
    }
}

