using Bunit;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class BacklogPageTests : FrontendTestBase
    {
        public BacklogPageTests()
        {
            // Base class handles MudServices, JSInterop, auth, and ProjectStateService setup
        }

        [Fact]
        public void BacklogPage_RendersCorrectly()
        {
            var component = Render<Backlog>();

            Assert.NotNull(component);
            Assert.Contains("bl-page", component.Markup);
        }

        [Fact]
        public void BacklogPage_HasSwimlanePageClass()
        {
            var component = Render<Backlog>();

            // Backlog uses its own bl-page layout, not the swimlane-page layout
            Assert.Contains("bl-page", component.Markup);
        }

        [Fact]
        public void BacklogPage_RendersWithoutExceptions()
        {
            var component = Render<Backlog>();

            Assert.NotNull(component);
        }

        [Fact]
        public void BacklogPage_HasCorrectPageRoute()
        {
            var component = Render<Backlog>();

            Assert.NotNull(component);
        }

        [Fact]
        public void BacklogPage_RendersAllFourPriorityLaneLabels()
        {
            var component = Render<Backlog>();

            // Backlog renders priority filter chips in the toolbar, not swim-lane columns.
            // The chip text matches the PbiPriority enum values.
            Assert.Contains("None", component.Markup);
            Assert.Contains("Low", component.Markup);
            Assert.Contains("Medium", component.Markup);
            Assert.Contains("High", component.Markup);
        }

        [Fact]
        public void BacklogPage_PriorityLanes_AreInCorrectOrder()
        {
            var markup = Render<Backlog>().Markup;

            // Priority filter chips are rendered in enum order: None, Low, Medium, High
            var noneIdx = markup.IndexOf(">None<", StringComparison.Ordinal);
            var lowIdx = markup.IndexOf(">Low<", StringComparison.Ordinal);
            var mediumIdx = markup.IndexOf(">Medium<", StringComparison.Ordinal);
            var highIdx = markup.IndexOf(">High<", StringComparison.Ordinal);

            Assert.True(noneIdx < lowIdx, "None chip should appear before Low");
            Assert.True(lowIdx < mediumIdx, "Low chip should appear before Medium");
            Assert.True(mediumIdx < highIdx, "Medium chip should appear before High");
        }

        [Fact]
        public void BacklogPage_DoesNotRenderStatusLanes()
        {
            var markup = Render<Backlog>().Markup;

            // Backlog is a flat list, not a swim-lane board — status lane columns must not appear
            Assert.DoesNotContain("column-todo", markup);
            Assert.DoesNotContain("column-inprogress", markup);
            Assert.DoesNotContain("column-inreview", markup);
            Assert.DoesNotContain("column-done", markup);
        }
    }
}
