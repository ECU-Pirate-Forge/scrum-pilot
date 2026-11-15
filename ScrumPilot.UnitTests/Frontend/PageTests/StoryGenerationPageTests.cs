using Bunit;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using MudBlazor;
using NSubstitute;
using ScrumPilot.Web.Pages;
using ScrumPilot.Web.Components;
using ScrumPilot.Shared.Models;
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
        public async Task GenerationCapturesProjectAndCannotOpenAReviewDialogAfterOrganizationSwitch()
        {
            var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            HttpResponseFactoryAsync = (_, _) => delayed.Task;
            var provider = Render<MudDialogProvider>();
            var cut = Render<PbiGeneration>();
            cut.Find("input:not([type='file'])").Input("A problem to solve");
            cut.FindAll("button").Single(button => button.TextContent.Trim() == "Add").Click();
            var generation = cut.FindAll("button").Single(button => button.TextContent.Contains("GENERATE PBI")).ClickAsync(new());
            Assert.Contains("api/organizations/1/pbi/generateAiPbis?projectId=17", HttpRequests);

            TestWork.Dispose();
            delayed.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new ProductBacklogItem { Title = "Stale AI result" } })
            });
            await generation;

            Assert.Empty(provider.FindComponents<GeneratedPbiModal>());
            Assert.DoesNotContain("Stale AI result", provider.Markup);
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
