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
            var flagged   = CreateTestPbi();
            flagged.IsFlagged = true;

            var unflaggedMarkup = Render<PbiCard>(p => p.Add(x => x.PbiModel, unflagged)).Markup;
            var flaggedMarkup   = Render<PbiCard>(p => p.Add(x => x.PbiModel, flagged)).Markup;

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
    }
}

