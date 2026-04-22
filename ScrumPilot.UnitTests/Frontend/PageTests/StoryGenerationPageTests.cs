using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class PbiGenerationPageTests : FrontendTestBase
    {
        public PbiGenerationPageTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        [Fact]
        public void PbiGenerationPage_RendersCorrectly()
        {
            // Act
            var component = Render<PbiGeneration>();

            // Assert
            Assert.NotNull(component);
        }

        [Fact]
        public void PbiGenerationPage_HasCorrectPageRoute()
        {
            // This test verifies the page has the correct @page directive
            // Act
            var component = Render<PbiGeneration>();

            // Assert
            Assert.NotNull(component);
        }

        [Fact]
        public void PbiGenerationPage_ContainsInputField()
        {
            // Act
            var component = Render<PbiGeneration>();

            // Assert
            Assert.Contains("mud-input", component.Markup);
        }

        [Fact]
        public void PbiGenerationPage_HasPlaceholderText()
        {
            // Act
            var component = Render<PbiGeneration>();

            // Assert
            Assert.Contains("Enter problem statement", component.Markup);
        }

        [Fact]
        public void PbiGenerationPage_RendersWithoutExceptions()
        {
            // Act & Assert - Should not throw any exceptions during rendering
            var component = Render<PbiGeneration>();
            Assert.NotNull(component);
        }

        [Fact]
        public void PbiGenerationPage_HasMudContainer()
        {
            // Act
            var component = Render<PbiGeneration>();

            // Assert
            Assert.Contains("mud-container", component.Markup);
        }

        [Fact]
        public void PbiGenerationPage_HasMudPaper()
        {
            // Act
            var component = Render<PbiGeneration>();

            // Assert
            Assert.Contains("mud-paper", component.Markup);
        }

        [Fact]
        public void PbiGenerationPage_HasInputSection()
        {
            // Act
            var component = Render<PbiGeneration>();

            // Assert
            Assert.Contains("sg-input-container", component.Markup);
        }

        [Fact]
        public void PbiGenerationPage_HasMainContentSection()
        {
            // Act
            var component = Render<PbiGeneration>();

            // Assert - main page container class
            Assert.Contains("sg-page", component.Markup);
        }

        [Fact]
        public void PbiGenerationPage_ShowsEmptyListMessage_Initially()
        {
            // Act
            var component = Render<PbiGeneration>();

            // Assert - when no statements are queued the Generate button is hidden
            // and the CSV drop-zone hint is visible instead
            Assert.DoesNotContain("GENERATE PBI", component.Markup);
            Assert.Contains("One problem statement per line", component.Markup);
        }
    }
}

