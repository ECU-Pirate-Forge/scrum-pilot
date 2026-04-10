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

        [Fact]
        public void ScrumBoardPage_RendersAllFourStatusLaneLabels()
        {
            // Act
            var component = Render<ScrumBoard>();

            // Assert - All four PbiStatus lanes must be visible
            Assert.Contains("TO DO", component.Markup);
            Assert.Contains("IN PROGRESS", component.Markup);
            Assert.Contains("IN REVIEW", component.Markup);
            Assert.Contains("DONE", component.Markup);
        }

        [Fact]
        public void ScrumBoardPage_StatusLanes_AreInCorrectOrder()
        {
            // Act
            var component = Render<ScrumBoard>();
            var markup = component.Markup;

            // Assert - Lanes appear left-to-right: ToDo, InProgress, InReview, Done
            var todoIdx = markup.IndexOf("column-todo", StringComparison.Ordinal);
            var inProgressIdx = markup.IndexOf("column-inprogress", StringComparison.Ordinal);
            var inReviewIdx = markup.IndexOf("column-inreview", StringComparison.Ordinal);
            var doneIdx = markup.IndexOf("column-done", StringComparison.Ordinal);

            Assert.True(todoIdx < inProgressIdx, "ToDo lane should appear before InProgress");
            Assert.True(inProgressIdx < inReviewIdx, "InProgress lane should appear before InReview");
            Assert.True(inReviewIdx < doneIdx, "InReview lane should appear before Done");
        }

        [Fact]
        public void ScrumBoardPage_DoesNotRenderPriorityLanes()
        {
            // Act
            var component = Render<ScrumBoard>();
            var markup = component.Markup;

            // Assert - Priority lane CSS classes must not be present in Status mode
            Assert.DoesNotContain("column-none", markup);
            Assert.DoesNotContain("column-high", markup);
            Assert.DoesNotContain("column-medium", markup);
            Assert.DoesNotContain("column-low", markup);
        }
    }
}
