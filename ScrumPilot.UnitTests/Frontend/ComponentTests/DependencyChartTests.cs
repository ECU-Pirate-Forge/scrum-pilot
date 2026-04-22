using Bunit;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.ComponentTests
{
    public class DependencyChartTests : FrontendTestBase
    {
        public DependencyChartTests()
        {
            // Base class handles MudServices, JSInterop, auth, and ProjectStateService setup
        }

        private static ProductBacklogItem MakePbi(int id, string title, PbiStatus status = PbiStatus.ToDo, int? dependsOn = null) =>
            new()
            {
                PbiId        = id,
                Title        = title,
                Status       = status,
                Priority     = PbiPriority.Medium,
                DateCreated  = DateTime.UtcNow,
                LastUpdated  = DateTime.UtcNow,
                DependsOnPbiId = dependsOn
            };

        // ── Empty state ─────────────────────────────────────────────────────────

        [Fact]
        public void DependencyChart_RendersWithoutExceptions_WhenNoPbis()
        {
            var component = Render<DependencyChart>(p => p.Add(x => x.Pbis, []));

            Assert.NotNull(component);
            Assert.Contains("No PBIs available to chart", component.Markup);
        }

        // ── No dependencies state ───────────────────────────────────────────────

        [Fact]
        public void DependencyChart_ShowsInfoAlert_WhenNoDependenciesSet()
        {
            var pbis = new List<ProductBacklogItem>
            {
                MakePbi(1, "Story A"),
                MakePbi(2, "Story B")
            };

            var component = Render<DependencyChart>(p => p.Add(x => x.Pbis, pbis));

            Assert.Contains("None of the PBIs in this sprint have dependencies set", component.Markup);
        }

        // ── With dependencies ───────────────────────────────────────────────────

        [Fact]
        public void DependencyChart_RendersChartContainer_WhenDependenciesExist()
        {
            var pbis = new List<ProductBacklogItem>
            {
                MakePbi(1, "Blocked Story", dependsOn: 2),
                MakePbi(2, "Blocking Story")
            };

            var component = Render<DependencyChart>(p => p.Add(x => x.Pbis, pbis));

            // The chart container div should be in the markup
            Assert.Contains("dep-chart-", component.Markup);
        }

        [Fact]
        public void DependencyChart_DoesNotShowNoPbisMessage_WhenPbisProvided()
        {
            var pbis = new List<ProductBacklogItem> { MakePbi(1, "Story A") };

            var component = Render<DependencyChart>(p => p.Add(x => x.Pbis, pbis));

            Assert.DoesNotContain("No PBIs available to chart", component.Markup);
        }

        // ── IsFlagged indicator ─────────────────────────────────────────────────

        [Fact]
        public void DependencyChart_DoesNotThrow_WhenFlaggedPbiIncluded()
        {
            var flagged = MakePbi(1, "Flagged Story");
            flagged.IsFlagged = true;
            var pbis = new List<ProductBacklogItem> { flagged, MakePbi(2, "Normal Story", dependsOn: 1) };

            // Should render without exceptions — flagged nodes use different styling
            var component = Render<DependencyChart>(p => p.Add(x => x.Pbis, pbis));

            Assert.NotNull(component);
        }

        // ── Done node indicator ─────────────────────────────────────────────────

        [Fact]
        public void DependencyChart_DoesNotThrow_WhenDonePbiIncluded()
        {
            var done = MakePbi(1, "Completed Story", PbiStatus.Done);
            var pbis = new List<ProductBacklogItem> { done, MakePbi(2, "Active Story", dependsOn: 1) };

            var component = Render<DependencyChart>(p => p.Add(x => x.Pbis, pbis));

            Assert.NotNull(component);
        }
    }
}
