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
    }
}

