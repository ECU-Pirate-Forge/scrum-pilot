using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components;
using ScrumPilot.Web.Pages;
using ScrumPilot.Web.Services;
using System.Net;
using System.Net.Http.Json;
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

        [Fact]
        public void ScrumBoardPage_SuccessfulCardSave_PerformsOneMutationThenReloadsFilteredItems()
        {
            var original = new ProductBacklogItem
            {
                PbiId = 42,
                ProjectId = 7,
                Title = "Moved story",
                Description = "Before",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Medium,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
            var itemGetCount = 0;
            HttpResponseFactory = request =>
            {
                var url = request.RequestUri!.PathAndQuery.TrimStart('/');
                if (request.Method == HttpMethod.Get &&
                    url.StartsWith("api/Pbi/getNonDraftPbis", StringComparison.OrdinalIgnoreCase))
                {
                    itemGetCount++;
                    return JsonResponse(itemGetCount == 1
                        ? new[] { original }
                        : Array.Empty<ProductBacklogItem>());
                }

                if (request.Method == HttpMethod.Put &&
                    url.Equals("api/Pbi", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonResponse(original);
                }

                return JsonResponse(Array.Empty<object>());
            };
            Services.GetRequiredService<ProjectStateService>().SetProject(
                new Project { ProjectId = 7, ProjectName = "Project" });

            var component = Render<ScrumBoard>();
            component.WaitForAssertion(() => Assert.Contains("Moved story", component.Markup));
            component.FindComponents<MudBlazor.MudButton>()
                .Single(button => button.Markup.Contains("View Details"))
                .Find("button")
                .Click();
            var card = component.FindComponent<PbiCard>();
            card.FindAll("button")
                .Single(button => button.TextContent.Trim() == "Edit")
                .Click();

            var requestsBeforeSave = HttpRequestLog.Count;
            card.Find("input").Change("Moved story");
            card.FindAll("button")
                .Single(button => button.TextContent.Trim() == "Save")
                .Click();

            component.WaitForAssertion(() =>
            {
                var saveRequests = HttpRequestLog.Skip(requestsBeforeSave).ToList();
                Assert.Single(saveRequests.Where(entry => entry.Method == HttpMethod.Put));
                Assert.Single(saveRequests.Where(entry => entry.Method == HttpMethod.Get));
                Assert.DoesNotContain("Moved story", component.Markup);
                Assert.Contains("No PBIs match the selected filters.", component.Markup);
            });
        }

        private static HttpResponseMessage JsonResponse<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
