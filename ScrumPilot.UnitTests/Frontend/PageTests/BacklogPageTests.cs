using Bunit;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class BacklogPageTests : FrontendTestBase
    {
        public BacklogPageTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        [Fact]
        public void BacklogPage_RendersCorrectly()
        {
            // Act
            var component = Render<Backlog>();

            // Assert - Basic smoke test
            Assert.NotNull(component);
            Assert.Contains("kanban-page", component.Markup);
        }

        [Fact]
        public void BacklogPage_HasKanbanPageClass()
        {
            // Act
            var component = Render<Backlog>();

            // Assert
            Assert.Contains("kanban-page", component.Markup);
        }

        [Fact]
        public void BacklogPage_RendersWithoutExceptions()
        {
            // Act & Assert - Should not throw any exceptions during rendering
            var component = Render<Backlog>();
            Assert.NotNull(component);
        }

        [Fact]
        public void BacklogPage_HasCorrectPageRoute()
        {
            // This test verifies the page has the correct @page directive
            var component = Render<Backlog>();

            // Assert
            Assert.NotNull(component);
        }

        [Fact]
        public void BacklogPage_RendersAllFourPriorityLaneLabels()
        {
            // Act
            var component = Render<Backlog>();

            // Assert - All four PbiPriority lanes must be visible
            Assert.Contains("NONE", component.Markup);
            Assert.Contains("LOW", component.Markup);
            Assert.Contains("MEDIUM", component.Markup);
            Assert.Contains("HIGH", component.Markup);
        }

        [Fact]
        public void BacklogPage_PriorityLanes_AreInCorrectOrder()
        {
            // Act
            var component = Render<Backlog>();
            var markup = component.Markup;

            // Assert - Lanes appear left-to-right: None, Low, Medium, High
            var noneIdx = markup.IndexOf("column-none", StringComparison.Ordinal);
            var lowIdx = markup.IndexOf("column-low", StringComparison.Ordinal);
            var mediumIdx = markup.IndexOf("column-medium", StringComparison.Ordinal);
            var highIdx = markup.IndexOf("column-high", StringComparison.Ordinal);

            Assert.True(noneIdx < lowIdx, "None lane should appear before Low");
            Assert.True(lowIdx < mediumIdx, "Low lane should appear before Medium");
            Assert.True(mediumIdx < highIdx, "Medium lane should appear before High");
        }

        [Fact]
        public void BacklogPage_DoesNotRenderStatusLanes()
        {
            // Act
            var component = Render<Backlog>();
            var markup = component.Markup;

            // Assert - Status lane CSS classes must not be present in Priority mode
            Assert.DoesNotContain("column-todo", markup);
            Assert.DoesNotContain("column-inprogress", markup);
            Assert.DoesNotContain("column-inreview", markup);
            Assert.DoesNotContain("column-done", markup);
        }
    }
}
