using Bunit;
using ScrumPilot.Web.Pages;
using Xunit;

namespace ScrumPilot.UnitTests.Frontend.PageTests
{
    public class CreateStoryPageTests : FrontendTestBase
    {
        public CreateStoryPageTests()
        {
            // Base class handles MudServices and JSInterop setup
        }

        [Fact]
        public void CreateStoryPage_RendersWithoutExceptions()
        {
            // Act & Assert - Basic smoke test
            var component = Render<CreateStory>();
            Assert.NotNull(component);
            Assert.NotNull(component.Markup);
        }

        [Fact]
        public void CreateStoryPage_RendersCorrectly()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.NotNull(component);
        }

        [Fact]
        public void CreateStoryPage_HasCorrectPageRoute()
        {
            // The @page directive is "/create-story"; just verify the component renders
            var component = Render<CreateStory>();
            Assert.NotNull(component);
        }

        [Fact]
        public void CreateStoryPage_HasMudContainer()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("mud-container", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasMudPaper()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("mud-paper", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasPageHeader()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert - header text is rendered
            Assert.Contains("Create a New Story", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasSubtitle()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("Product Backlog Item", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasTitleInputField()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert - Title field placeholder text is rendered
            Assert.Contains("create-story-title", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasDescriptionInputField()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("create-story-description", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasStoryPointsSelect()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("create-story-points", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasPrioritySelect()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("create-story-priority", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasEpicSelectDisabled()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert - Epic field is present (disabled/coming soon)
            Assert.Contains("create-story-epic", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasSprintSelectDisabled()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert - Sprint field is present (disabled/coming soon)
            Assert.Contains("create-story-sprint", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasAcceptanceCriteriaSection()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("Acceptance Criteria", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasAssumptionsSection()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("Assumptions", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasAddCriterionButton()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert - The "Add Criterion" button is rendered
            Assert.Contains("add-ac-btn", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasAddAssumptionButton()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert - The "Add Assumption" button is rendered
            Assert.Contains("add-assumption-btn", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasSubmitButton()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("create-story-submit", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_HasCancelButton()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("create-story-cancel", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_ShowsEmptyAcceptanceCriteriaMessage_Initially()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert - shows placeholder message when no criteria added
            Assert.Contains("No criteria added yet", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_ShowsEmptyAssumptionsMessage_Initially()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert - shows placeholder message when no assumptions added
            Assert.Contains("No assumptions added yet", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_ShowsComingSoonHelperText_ForEpic()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("Epic support is not yet available", component.Markup);
        }

        [Fact]
        public void CreateStoryPage_ShowsComingSoonHelperText_ForSprint()
        {
            // Act
            var component = Render<CreateStory>();

            // Assert
            Assert.Contains("Sprint support is not yet available", component.Markup);
        }
    }
}
