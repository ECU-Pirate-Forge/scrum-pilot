using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class StoryControllerTests
    {
        private readonly IStoryService _mockStoryService;
        private readonly StoryController _controller;

        public StoryControllerTests()
        {
            _mockStoryService = Substitute.For<IStoryService>();
            _controller = new StoryController(_mockStoryService);
        }

        [Fact]
        public async Task GetAllStories_ReturnsOkResult_WithListOfStories()
        {
            // Arrange
            var expectedStories = new List<ProductBacklogItem>
            {
                new ProductBacklogItem 
                { 
                    PbiId = 1, 
                    Title = "Test Story 1", 
                    Description = "Test Description 1",
                    Status = PbiStatus.ToDo,
                    Priority = PbiPriority.Low,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                },
                new ProductBacklogItem 
                { 
                    PbiId = 2, 
                    Title = "Test Story 2", 
                    Description = "Test Description 2",
                    Status = PbiStatus.InProgress,
                    Priority = PbiPriority.High,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _mockStoryService.GetAllStoriesAsync().Returns(expectedStories);

            // Act
            var result = await _controller.GetAllStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(expectedStories.Count, actualStories.Count);
            Assert.Equal(expectedStories, actualStories);
            await _mockStoryService.Received(1).GetAllStoriesAsync();
        }

        [Fact]
        public async Task GetAllStories_ReturnsOkResult_WithEmptyList_WhenNoStories()
        {
            // Arrange
            var expectedStories = new List<ProductBacklogItem>();
            _mockStoryService.GetAllStoriesAsync().Returns(expectedStories);

            // Act
            var result = await _controller.GetAllStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Empty(actualStories);
            await _mockStoryService.Received(1).GetAllStoriesAsync();
        }





        [Fact]
        public async Task GetDraftStories_ReturnsOkResult_WithDraftStories()
        {
            // Arrange
            var expectedDraftStories = new List<ProductBacklogItem>
            {
                new ProductBacklogItem 
                { 
                    PbiId = 1, 
                    Title = "Draft Story 1", 
                    Description = "Draft Description 1",
                    Status = PbiStatus.ToDo,
                    Priority = PbiPriority.Low,
                    IsDraft = true,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _mockStoryService.GetDraftStoriesAsync().Returns(expectedDraftStories);

            // Act
            var result = await _controller.GetDraftStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualStories);
            Assert.True(actualStories[0].IsDraft);
            await _mockStoryService.Received(1).GetDraftStoriesAsync();
        }

        [Fact]
        public async Task GetDraftStories_ReturnsOkResult_WithEmptyList_WhenNoDraftStories()
        {
            // Arrange
            var expectedStories = new List<ProductBacklogItem>();
            _mockStoryService.GetDraftStoriesAsync().Returns(expectedStories);

            // Act
            var result = await _controller.GetDraftStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Empty(actualStories);
            await _mockStoryService.Received(1).GetDraftStoriesAsync();
        }





        [Fact]
        public async Task GenerateAiStory_ReturnsOkResult_WithGeneratedStories_WhenValidProblemStatements()
        {
            // Arrange
            var problemStatements = new List<string> { "As a user, I want to log in to the system" };
            var expectedStories = new List<ProductBacklogItem>
            {
                new ProductBacklogItem
                {
                    PbiId = 1,
                    Title = "User Login Story",
                    Description = "Generated story description",
                    Status = PbiStatus.ToDo,
                    Priority = PbiPriority.Low,
                    Origin = PbiOrigin.AiGenerated,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _mockStoryService.GenerateAiStories(problemStatements).Returns(expectedStories);

            // Act
            var result = await _controller.GenerateAiStories(problemStatements);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(expectedStories[0].PbiId, actualStories[0].PbiId);
            Assert.Equal(expectedStories[0].Title, actualStories[0].Title);
            Assert.Equal(expectedStories[0].Origin, actualStories[0].Origin);
            await _mockStoryService.Received(1).GenerateAiStories(problemStatements);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsBadRequest_WhenProblemStatementsIsNullOrEmpty()
        {
            // Act
            var resultNull = await _controller.GenerateAiStories(null!);
            var resultEmpty = await _controller.GenerateAiStories(new List<string>());

            // Assert
            Assert.Equal("At least one problem statement is required.", Assert.IsType<BadRequestObjectResult>(resultNull.Result).Value);
            Assert.Equal("At least one problem statement is required.", Assert.IsType<BadRequestObjectResult>(resultEmpty.Result).Value);
            await _mockStoryService.DidNotReceive().GenerateAiStories(Arg.Any<List<string>>());
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public async Task GenerateAiStory_ReturnsBadRequest_WhenAnyProblemStatementIsNullOrWhitespace(string? invalidStatement)
        {
            // Arrange
            var problemStatements = new List<string> { "Valid statement", invalidStatement! };

            // Act
            var result = await _controller.GenerateAiStories(problemStatements);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal("All problem statements must be non-empty strings.", badRequestResult.Value);
            await _mockStoryService.DidNotReceive().GenerateAiStories(Arg.Any<List<string>>());
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsBadRequest_WhenInvalidOperationExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Invalid operation occurred";
            _mockStoryService.GenerateAiStories(problemStatements)
                .Returns(Task.FromException<List<ProductBacklogItem>>(new InvalidOperationException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStories(problemStatements);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal($"Failed to generate AI story: {exceptionMessage}", badRequestResult.Value);
            await _mockStoryService.Received(1).GenerateAiStories(problemStatements);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode502_WhenHttpRequestExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Network error";
            _mockStoryService.GenerateAiStories(problemStatements)
                .Returns(Task.FromException<List<ProductBacklogItem>>(new HttpRequestException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStories(problemStatements);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(502, statusCodeResult.StatusCode);
            Assert.Equal($"Failed to communicate with Ollama service: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStories(problemStatements);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode408_WhenTimeoutExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Request timed out";
            _mockStoryService.GenerateAiStories(problemStatements)
                .Returns(Task.FromException<List<ProductBacklogItem>>(new TimeoutException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStories(problemStatements);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(408, statusCodeResult.StatusCode);
            Assert.Equal($"Request timed out: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStories(problemStatements);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode500_WhenUnexpectedExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Unexpected error";
            _mockStoryService.GenerateAiStories(problemStatements)
                .Returns(Task.FromException<List<ProductBacklogItem>>(new Exception(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStories(problemStatements);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal($"An unexpected error occurred: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStories(problemStatements);
        }





        [Fact]
        public async Task CreateStory_ReturnsOkResult_WithCreatedStory()
        {
            // Arrange
            var inputStory = new ProductBacklogItem
            {
                Title = "New Story",
                Description = "New Description",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Medium
            };

            var createdStory = new ProductBacklogItem
            {
                PbiId = 1,
                Title = inputStory.Title,
                Description = inputStory.Description,
                Status = inputStory.Status,
                Priority = inputStory.Priority,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _mockStoryService.CreateStoryAsync(inputStory).Returns(createdStory);

            // Act
            var result = await _controller.CreateStory(inputStory);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStory = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(createdStory.PbiId, actualStory.PbiId);
            Assert.Equal(createdStory.Title, actualStory.Title);
            await _mockStoryService.Received(1).CreateStoryAsync(inputStory);
        }





        [Fact]
        public async Task UpdateStory_ReturnsOkResult_WithUpdatedStory()
        {
            // Arrange
            var updatedStory = new ProductBacklogItem
            {
                PbiId = 1,
                Title = "Updated Story",
                Description = "Updated Description",
                Status = PbiStatus.InProgress,
                Priority = PbiPriority.High,
                DateCreated = DateTime.UtcNow.AddDays(-1),
                LastUpdated = DateTime.UtcNow
            };

            _mockStoryService.UpdateStoryAsync(updatedStory).Returns(updatedStory);

            // Act
            var result = await _controller.UpdateStory(updatedStory);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStory = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(updatedStory.PbiId, actualStory.PbiId);
            Assert.Equal(updatedStory.Title, actualStory.Title);
            Assert.Equal(PbiStatus.InProgress, actualStory.Status);
            await _mockStoryService.Received(1).UpdateStoryAsync(updatedStory);
        }

        [Fact]
        public async Task UpdateStory_ReturnsOkResult_WhenStatusIsInReview()
        {
            // Arrange - ScrumBoard drag-and-drop can move a card into the InReview lane
            var story = new ProductBacklogItem
            {
                PbiId = 2,
                Title = "Story Under Review",
                Description = "In review description",
                Status = PbiStatus.InReview,
                Priority = PbiPriority.Medium,
                DateCreated = DateTime.UtcNow.AddDays(-2),
                LastUpdated = DateTime.UtcNow
            };

            _mockStoryService.UpdateStoryAsync(story).Returns(story);

            // Act
            var result = await _controller.UpdateStory(story);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStory = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(PbiStatus.InReview, actualStory.Status);
            await _mockStoryService.Received(1).UpdateStoryAsync(story);
        }

        [Fact]
        public async Task UpdateStory_ReturnsOkResult_WhenPriorityIsNone()
        {
            // Arrange - Backlog drag-and-drop can move a card into the None priority lane
            var story = new ProductBacklogItem
            {
                PbiId = 3,
                Title = "Untriaged Story",
                Description = "Priority not yet assigned",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.None,
                DateCreated = DateTime.UtcNow.AddDays(-1),
                LastUpdated = DateTime.UtcNow
            };

            _mockStoryService.UpdateStoryAsync(story).Returns(story);

            // Act
            var result = await _controller.UpdateStory(story);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStory = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(PbiPriority.None, actualStory.Priority);
            await _mockStoryService.Received(1).UpdateStoryAsync(story);
        }

        [Fact]
        public async Task CommitDraftStory_ReturnsOkResult_WithCommittedStory()
        {
            // Arrange
            var draftStory = new ProductBacklogItem
            {
                PbiId = 7,
                Title = "Draft Story",
                Description = "Draft Description",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Medium,
                IsDraft = true,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            var committedStory = new ProductBacklogItem
            {
                PbiId = draftStory.PbiId,
                Title = draftStory.Title,
                Description = draftStory.Description,
                Status = draftStory.Status,
                Priority = draftStory.Priority,
                IsDraft = false,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _mockStoryService.CommitStoryAsync(draftStory).Returns(committedStory);

            // Act
            var result = await _controller.CommitStory(draftStory);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStory = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(committedStory.PbiId, actualStory.PbiId);
            Assert.False(actualStory.IsDraft);
            await _mockStoryService.Received(1).CommitStoryAsync(draftStory);
        }

        [Fact]
        public async Task CommitDraftStory_ReturnsNotFound_WhenDraftStoryMissing()
        {
            // Arrange
            var draftStory = new ProductBacklogItem
            {
                PbiId = 99,
                Title = "Missing Draft",
                Description = "Missing",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Low,
                IsDraft = true,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _mockStoryService
                .CommitStoryAsync(draftStory)
                .Returns(Task.FromException<ProductBacklogItem>(new KeyNotFoundException("Draft story not found.")));

            // Act
            var result = await _controller.CommitStory(draftStory);

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
            Assert.Equal("Draft story not found.", notFoundResult.Value);
            await _mockStoryService.Received(1).CommitStoryAsync(draftStory);
        }





        [Fact]
        public async Task DeleteStory_ReturnsNoContent_WhenSuccessfullyDeleted()
        {
            // Arrange
            var storyId = 1;
            _mockStoryService.DeleteStoryAsync(storyId).Returns(true);

            // Act
            var result = await _controller.DeleteStory(storyId);

            // Assert
            Assert.IsType<NoContentResult>(result);
            await _mockStoryService.Received(1).DeleteStoryAsync(storyId);
        }

        [Fact]
        public async Task DeleteStory_ReturnsNotFound_WhenStoryDoesNotExist()
        {
            // Arrange
            var storyId = 999;
            _mockStoryService.DeleteStoryAsync(storyId).Returns(false);

            // Act
            var result = await _controller.DeleteStory(storyId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            await _mockStoryService.Received(1).DeleteStoryAsync(storyId);
        }


    }
}
