using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.ComponentTests
{
    public class StoryCardTests : FrontendTestBase
    {
        public StoryCardTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        private ProductBacklogItem CreateTestStory()
        {
            return new ProductBacklogItem
            {
                PbiId = 1,
                Title = "Test Story",
                Description = "Test Description",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Medium,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
        }

        [Fact]
        public void StoryCard_RendersWithValidStory()
        {
            // Arrange
            var story = CreateTestStory();

            // Act
            var component = Render<StoryCard>(parameters => parameters
                .Add(p => p.StoryModel, story));

            // Assert - Focus on content rendering, not interactive elements
            Assert.Contains("Test Story", component.Markup);
            Assert.Contains("Test Description", component.Markup);
            Assert.Contains("mud-paper", component.Markup);
        }

        [Fact]
        public void StoryCard_HasEditButton()
        {
            // Arrange
            var story = CreateTestStory();

            // Act
            var component = Render<StoryCard>(parameters => parameters
                .Add(p => p.StoryModel, story));

            // Assert - Check for edit functionality
            Assert.Contains("Edit", component.Markup);
        }

        [Fact]
        public void StoryCard_HasCorrectContainerStructure()
        {
            // Arrange
            var story = CreateTestStory();

            // Act
            var component = Render<StoryCard>(parameters => parameters
                .Add(p => p.StoryModel, story));

            // Assert
            Assert.Contains("mud-container", component.Markup);
            Assert.Contains("slideInUp", component.Markup); // Animation class
        }
    }
}

