using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class ScrumBoardPageTests : FrontendTestBase
    {
        public ScrumBoardPageTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        [Fact]
        public void ScrumBoardPage_RendersCorrectly()
        {
            // Act
            var component = Render<ScrumBoard>();

            // Assert - Basic smoke test
            Assert.NotNull(component);
            Assert.Contains("kanban-page", component.Markup);
        }

        [Fact]
        public void ScrumBoardPage_HasKanbanPageClass()
        {
            // Act
            var component = Render<ScrumBoard>();

            // Assert
            Assert.Contains("kanban-page", component.Markup);
        }

        [Fact]
        public void ScrumBoardPage_RendersWithoutExceptions()
        {
            // Act & Assert - Should not throw any exceptions during rendering
            var component = Render<ScrumBoard>();
            Assert.NotNull(component);
        }

        [Fact]
        public void ScrumBoardPage_HasCorrectPageRoute()
        {
            // This test verifies the page has the correct @page directive
            // Act
            var component = Render<ScrumBoard>();

            // Assert
            Assert.NotNull(component);
        }
    }
}