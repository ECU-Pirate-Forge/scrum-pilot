using Bunit;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.ComponentTests
{
    public class SwimLanesTests : FrontendTestBase
    {
        public SwimLanesTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        private static List<ProductBacklogItem> EmptyItems() => [];

        private static ProductBacklogItem MakeItem(int id, string title, PbiStatus status, PbiPriority priority) =>
            new()
            {
                PbiId = id,
                Title = title,
                Status = status,
                Priority = priority,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

        // ── Status mode (default) ───────────────────────────────────────────────

        [Fact]
        public void SwimLanes_StatusMode_RendersAllFourLaneLabels()
        {
            // Act
            var component = Render<SwimLanes>(p => p.Add(x => x.Items, EmptyItems()));

            // Assert
            Assert.Contains("TO DO", component.Markup);
            Assert.Contains("IN PROGRESS", component.Markup);
            Assert.Contains("IN REVIEW", component.Markup);
            Assert.Contains("DONE", component.Markup);
        }

        [Fact]
        public void SwimLanes_StatusMode_HasCorrectColumnCssClasses()
        {
            // Act
            var component = Render<SwimLanes>(p => p.Add(x => x.Items, EmptyItems()));

            // Assert
            Assert.Contains("column-todo", component.Markup);
            Assert.Contains("column-inprogress", component.Markup);
            Assert.Contains("column-inreview", component.Markup);
            Assert.Contains("column-done", component.Markup);
        }

        [Fact]
        public void SwimLanes_StatusMode_LanesAreInCorrectOrder()
        {
            // Act
            var markup = Render<SwimLanes>(p => p.Add(x => x.Items, EmptyItems())).Markup;

            // Assert - left-to-right: ToDo → InProgress → InReview → Done
            var todoIdx = markup.IndexOf("column-todo", StringComparison.Ordinal);
            var inProgressIdx = markup.IndexOf("column-inprogress", StringComparison.Ordinal);
            var inReviewIdx = markup.IndexOf("column-inreview", StringComparison.Ordinal);
            var doneIdx = markup.IndexOf("column-done", StringComparison.Ordinal);

            Assert.True(todoIdx < inProgressIdx, "ToDo should appear before InProgress");
            Assert.True(inProgressIdx < inReviewIdx, "InProgress should appear before InReview");
            Assert.True(inReviewIdx < doneIdx, "InReview should appear before Done");
        }

        [Fact]
        public void SwimLanes_DefaultMode_IsStatusMode()
        {
            // When GroupByPriority is not set it defaults to false (status mode)
            var markup = Render<SwimLanes>(p => p.Add(x => x.Items, EmptyItems())).Markup;

            Assert.Contains("column-todo", markup);
            Assert.DoesNotContain("column-none", markup);
            Assert.DoesNotContain("column-high", markup);
        }

        // ── Priority mode ───────────────────────────────────────────────────────

        [Fact]
        public void SwimLanes_PriorityMode_RendersAllFourLaneLabels()
        {
            // Act
            var component = Render<SwimLanes>(p => p
                .Add(x => x.Items, EmptyItems())
                .Add(x => x.GroupByPriority, true));

            // Assert
            Assert.Contains("NONE", component.Markup);
            Assert.Contains("LOW", component.Markup);
            Assert.Contains("MEDIUM", component.Markup);
            Assert.Contains("HIGH", component.Markup);
        }

        [Fact]
        public void SwimLanes_PriorityMode_HasCorrectColumnCssClasses()
        {
            // Act
            var component = Render<SwimLanes>(p => p
                .Add(x => x.Items, EmptyItems())
                .Add(x => x.GroupByPriority, true));

            // Assert
            Assert.Contains("column-none", component.Markup);
            Assert.Contains("column-low", component.Markup);
            Assert.Contains("column-medium", component.Markup);
            Assert.Contains("column-high", component.Markup);
        }

        [Fact]
        public void SwimLanes_PriorityMode_LanesAreInCorrectOrder()
        {
            // Act
            var markup = Render<SwimLanes>(p => p
                .Add(x => x.Items, EmptyItems())
                .Add(x => x.GroupByPriority, true)).Markup;

            // Assert - left-to-right: None → Low → Medium → High
            var noneIdx = markup.IndexOf("column-none", StringComparison.Ordinal);
            var lowIdx = markup.IndexOf("column-low", StringComparison.Ordinal);
            var mediumIdx = markup.IndexOf("column-medium", StringComparison.Ordinal);
            var highIdx = markup.IndexOf("column-high", StringComparison.Ordinal);

            Assert.True(noneIdx < lowIdx, "None should appear before Low");
            Assert.True(lowIdx < mediumIdx, "Low should appear before Medium");
            Assert.True(mediumIdx < highIdx, "Medium should appear before High");
        }

        [Fact]
        public void SwimLanes_PriorityMode_DoesNotRenderStatusLanes()
        {
            // Act
            var markup = Render<SwimLanes>(p => p
                .Add(x => x.Items, EmptyItems())
                .Add(x => x.GroupByPriority, true)).Markup;

            // Assert
            Assert.DoesNotContain("column-todo", markup);
            Assert.DoesNotContain("column-inprogress", markup);
            Assert.DoesNotContain("column-inreview", markup);
            Assert.DoesNotContain("column-done", markup);
        }

        // ── Item rendering ──────────────────────────────────────────────────────

        [Fact]
        public void SwimLanes_StatusMode_RendersItemTitle()
        {
            // Arrange
            var items = new List<ProductBacklogItem>
            {
                MakeItem(1, "My First Story", PbiStatus.ToDo, PbiPriority.Medium)
            };

            // Act
            var component = Render<SwimLanes>(p => p.Add(x => x.Items, items));

            // Assert
            Assert.Contains("My First Story", component.Markup);
        }

        [Fact]
        public void SwimLanes_PriorityMode_RendersItemTitle()
        {
            // Arrange
            var items = new List<ProductBacklogItem>
            {
                MakeItem(1, "High Priority Task", PbiStatus.InProgress, PbiPriority.High)
            };

            // Act
            var component = Render<SwimLanes>(p => p
                .Add(x => x.Items, items)
                .Add(x => x.GroupByPriority, true));

            // Assert
            Assert.Contains("High Priority Task", component.Markup);
        }

        [Fact]
        public void SwimLanes_RendersWithoutExceptions_WhenItemsIsEmpty()
        {
            // Act & Assert - should not throw
            var component = Render<SwimLanes>(p => p.Add(x => x.Items, EmptyItems()));
            Assert.NotNull(component);
        }

        [Fact]
        public void SwimLanes_RendersWithoutExceptions_WhenGroupByPriorityAndItemsIsEmpty()
        {
            // Act & Assert - should not throw
            var component = Render<SwimLanes>(p => p
                .Add(x => x.Items, EmptyItems())
                .Add(x => x.GroupByPriority, true));
            Assert.NotNull(component);
        }
    }
}
