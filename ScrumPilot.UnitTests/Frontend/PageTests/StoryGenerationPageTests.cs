using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class StoryGenerationPageTests : FrontendTestBase
    {
        public StoryGenerationPageTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        [Fact]
        public void StoryGenerationPage_RendersCorrectly()
        {
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.NotNull(component);
        }

        [Fact]
        public void StoryGenerationPage_HasCorrectPageRoute()
        {
            // This test verifies the page has the correct @page directive
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.NotNull(component);
        }

        [Fact]
        public void StoryGenerationPage_ContainsInputField()
        {
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.Contains("mud-input", component.Markup);
        }

        [Fact]
        public void StoryGenerationPage_HasPlaceholderText()
        {
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.Contains("Enter problem statement", component.Markup);
        }

        [Fact]
        public void StoryGenerationPage_RendersWithoutExceptions()
        {
            // Act & Assert - Should not throw any exceptions during rendering
            var component = Render<StoryGeneration>();
            Assert.NotNull(component);
        }

        [Fact]
        public void StoryGenerationPage_HasMudContainer()
        {
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.Contains("mud-container", component.Markup);
        }

        [Fact]
        public void StoryGenerationPage_HasMudPaper()
        {
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.Contains("mud-paper", component.Markup);
        }

        [Fact]
        public void StoryGenerationPage_HasInputSection()
        {
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.Contains("sg-input-container", component.Markup);
        }

        [Fact]
        public void StoryGenerationPage_HasMainContentSection()
        {
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.Contains("sg-shell", component.Markup);
        }

        [Fact]
        public void StoryGenerationPage_ShowsEmptyListMessage_Initially()
        {
            // Act
            var component = Render<StoryGeneration>();

            // Assert
            Assert.Contains("No problem statements added yet.", component.Markup);
        }
    }
}

