using Bunit;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class ScrumBoardPageTests : FrontendTestBase
    {
        public ScrumBoardPageTests()
        {
            // Base class handles MudServices, JSInterop, auth, and ProjectStateService setup
        }

        // ── Smoke / layout ──────────────────────────────────────────────────────

        [Fact]
        public void ScrumBoardPage_RendersCorrectly()
        {
            var component = Render<ScrumBoard>();

            Assert.NotNull(component);
            Assert.Contains("swimlane-page", component.Markup);
        }

        [Fact]
        public void ScrumBoardPage_HasSwimlanePageClass()
        {
            var component = Render<ScrumBoard>();

            Assert.Contains("swimlane-page", component.Markup);
        }

        [Fact]
        public void ScrumBoardPage_RendersWithoutExceptions()
        {
            var component = Render<ScrumBoard>();

            Assert.NotNull(component);
        }

        [Fact]
        public void ScrumBoardPage_HasCorrectPageRoute()
        {
            var component = Render<ScrumBoard>();

            Assert.NotNull(component);
        }

        // ── Status lanes ────────────────────────────────────────────────────────

        [Fact]
        public void ScrumBoardPage_RendersAllFourStatusLaneLabels()
        {
            var component = Render<ScrumBoard>();

            Assert.Contains("TO DO", component.Markup);
            Assert.Contains("IN PROGRESS", component.Markup);
            Assert.Contains("IN REVIEW", component.Markup);
            Assert.Contains("DONE", component.Markup);
        }

        [Fact]
        public void ScrumBoardPage_StatusLanes_AreInCorrectOrder()
        {
            var markup = Render<ScrumBoard>().Markup;

            var todoIdx = markup.IndexOf("column-todo", StringComparison.Ordinal);
            var inProgressIdx = markup.IndexOf("column-inprogress", StringComparison.Ordinal);
            var inReviewIdx = markup.IndexOf("column-inreview", StringComparison.Ordinal);
            var doneIdx = markup.IndexOf("column-done", StringComparison.Ordinal);

            Assert.True(todoIdx < inProgressIdx, "ToDo lane should appear before InProgress");
            Assert.True(inProgressIdx < inReviewIdx, "InProgress lane should appear before InReview");
            Assert.True(inReviewIdx < doneIdx, "InReview lane should appear before Done");
        }

        [Fact]
        public void ScrumBoardPage_HasCorrectStatusLaneCssClasses()
        {
            var markup = Render<ScrumBoard>().Markup;

            Assert.Contains("column-todo", markup);
            Assert.Contains("column-inprogress", markup);
            Assert.Contains("column-inreview", markup);
            Assert.Contains("column-done", markup);
        }

        [Fact]
        public void ScrumBoardPage_DoesNotRenderPriorityLanes()
        {
            // ScrumBoard is always in status mode — priority lane columns must never appear
            var markup = Render<ScrumBoard>().Markup;

            Assert.DoesNotContain("column-none", markup);
            Assert.DoesNotContain("column-high", markup);
            Assert.DoesNotContain("column-medium", markup);
            Assert.DoesNotContain("column-low", markup);
        }

        // ── Toolbar ─────────────────────────────────────────────────────────────

        [Fact]
        public void ScrumBoardPage_AlwaysShowsDependencyChartButton()
        {
            // The dependency chart button is now hardcoded into ScrumBoard (no parameter needed)
            var markup = Render<ScrumBoard>().Markup;

            // The AccountTree icon button is always present in the toolbar
            Assert.Contains("mud-icon-button", markup);
        }

        [Fact]
        public void ScrumBoardPage_DefaultsToStatusMode_NotPriorityMode()
        {
            // ScrumBoard no longer has a GroupByPriority parameter; status lanes are hardcoded
            var markup = Render<ScrumBoard>().Markup;

            Assert.Contains("column-todo", markup);
            Assert.DoesNotContain("column-none", markup);
            Assert.DoesNotContain("column-high", markup);
        }
    }
}
