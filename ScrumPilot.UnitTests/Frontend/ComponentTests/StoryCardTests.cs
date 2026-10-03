using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.ComponentTests
{
    public class PbiCardTests : FrontendTestBase
    {
        public PbiCardTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        private ProductBacklogItem CreateTestPbi()
        {
            return new ProductBacklogItem
            {
                PbiId = 1,
                Title = "Test pbi",
                Description = "Test Description",
                Type = PbiType.Bug,
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Medium,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
        }

        [Fact]
        public void PbiCard_RendersWithValidPbi()
        {
            // Arrange
            var pbi = CreateTestPbi();

            // Act
            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, pbi));

            // Assert - Focus on content rendering, not interactive elements
            Assert.Contains("Test pbi", component.Markup);
            Assert.Contains("Test Description", component.Markup);
            Assert.Contains("Bug", component.Markup);
            Assert.Contains("mud-paper", component.Markup);
        }

        [Fact]
        public void PbiCard_HasEditButton()
        {
            // Arrange
            var pbi = CreateTestPbi();

            // Act
            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, pbi));

            // Assert - Check for edit functionality
            Assert.Contains("Edit", component.Markup);
        }

        [Fact]
        public void PbiCard_HasCorrectContainerStructure()
        {
            // Arrange
            var pbi = CreateTestPbi();

            // Act
            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, pbi));

            // Assert
            Assert.Contains("mud-container", component.Markup);
            Assert.Contains("slideInUp", component.Markup); // Animation class
        }

        [Fact]
        public void PbiCard_FlagButton_IsRendered_InViewMode()
        {
            var pbi = CreateTestPbi();

            var component = Render<PbiCard>(p => p.Add(x => x.PbiModel, pbi));

            // View mode always shows the flag icon button
            var iconButtons = component.FindAll(".mud-icon-button");
            Assert.NotEmpty(iconButtons);
        }

        [Fact]
        public void PbiCard_FlagButton_ShowsRedState_WhenPbiIsFlagged()
        {
            var unflagged = CreateTestPbi(); // IsFlagged = false
            var flagged = CreateTestPbi();
            flagged.IsFlagged = true;

            var unflaggedMarkup = Render<PbiCard>(p => p.Add(x => x.PbiModel, unflagged)).Markup;
            var flaggedMarkup = Render<PbiCard>(p => p.Add(x => x.PbiModel, flagged)).Markup;

            // The flagged and unflagged markups must differ — the icon button colour changes
            Assert.NotEqual(unflaggedMarkup, flaggedMarkup);
        }

        [Fact]
        public void PbiCard_FlagButton_ShowsDefaultState_WhenPbiIsNotFlagged()
        {
            var pbi = CreateTestPbi();
            pbi.IsFlagged = false;

            var component = Render<PbiCard>(p => p.Add(x => x.PbiModel, pbi));

            // When not flagged, the flag icon button must NOT use the error colour
            Assert.DoesNotContain("mud-error-text", component.Markup);
        }

        [Fact]
        public void PbiCard_HidesMutatingControls_WhenReadOnly()
        {
            var pbi = CreateTestPbi();

            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, pbi)
                .Add(p => p.ReadOnly, true));

            Assert.DoesNotContain(">Edit<", component.Markup);
            Assert.DoesNotContain("Improve with AI", component.Markup);
            Assert.DoesNotContain("Flag this PBI", component.Markup);
            Assert.Contains("Bug", component.Markup);
        }

        [Fact]
        public void PbiCard_FailedSave_KeepsEditModeAndDoesNotNotifyParent()
        {
            HttpResponseStatusCode = System.Net.HttpStatusCode.BadRequest;
            var pbi = CreateTestPbi();
            var notified = false;
            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, pbi)
                .Add(p => p.ProjectId, 1)
                .Add(p => p.StartInEditMode, true)
                .Add(p => p.OnSave, _ => notified = true));

            component.FindAll("button")
                .Single(button => button.TextContent.Trim() == "Save")
                .Click();

            component.WaitForAssertion(() =>
            {
                Assert.Contains("Save failed. Your changes were not applied.", component.Markup);
                Assert.Contains(component.FindAll("button"),
                    button => button.TextContent.Trim() == "Save");
                Assert.False(notified);
                Assert.Equal("Test pbi", pbi.Title);
            });
        }

        [Fact]
        public void PbiCard_SaveWithoutPersistence_UpdatesOriginalInstanceAndNotifiesParent()
        {
            var pbi = CreateTestPbi();
            ProductBacklogItem? notified = null;
            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, pbi)
                .Add(p => p.ProjectId, 1)
                .Add(p => p.StartInEditMode, true)
                .Add(p => p.PersistOnSave, false)
                .Add(p => p.OnSave, saved => notified = saved));
            var requestsBeforeSave = HttpRequests.Count;

            component.Find("input").Change("Edited title");
            component.FindAll("button")
                .Single(button => button.TextContent.Trim() == "Save")
                .Click();

            component.WaitForAssertion(() =>
            {
                Assert.Equal(requestsBeforeSave, HttpRequests.Count);
                Assert.Equal("Edited title", pbi.Title);
                Assert.Same(pbi, notified);
                Assert.DoesNotContain(component.FindAll("button"),
                    button => button.TextContent.Trim() == "Save");
            });
        }

        [Fact]
        public void PbiCard_DefaultSave_PersistsToApi()
        {
            var pbi = CreateTestPbi();
            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, pbi)
                .Add(p => p.ProjectId, 1)
                .Add(p => p.StartInEditMode, true));

            component.Find("input").Change("Persisted title");
            component.FindAll("button")
                .Single(button => button.TextContent.Trim() == "Save")
                .Click();

            component.WaitForAssertion(() =>
                Assert.Contains(HttpRequests, request =>
                    request.Equals("api/Pbi", StringComparison.OrdinalIgnoreCase)));
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 0)]
        public void PbiCard_HidesPersistenceActions_WhenCardCannotPersist(
            bool persistOnSave, int pbiId)
        {
            var pbi = CreateTestPbi();
            pbi.PbiId = pbiId;

            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, pbi)
                .Add(p => p.PersistOnSave, persistOnSave));

            Assert.DoesNotContain("Improve with AI", component.Markup);
            Assert.DoesNotContain(component.FindAll("button"),
                button => button.TextContent.Trim() == "Delete");
            Assert.Empty(component.FindAll(".mud-icon-button"));
            Assert.Empty(HttpRequests);
        }

        [Fact]
        public void PbiCard_ShowsPersistenceActions_WhenCardIsPersisted()
        {
            var component = Render<PbiCard>(parameters => parameters
                .Add(p => p.PbiModel, CreateTestPbi()));

            Assert.Contains("Improve with AI", component.Markup);
            Assert.Contains(component.FindAll("button"),
                button => button.TextContent.Trim() == "Delete");
            Assert.NotEmpty(component.FindAll(".mud-icon-button"));
        }
    }
}
