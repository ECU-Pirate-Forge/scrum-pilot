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
            var expectedStories = new List<Story>
            {
                new Story 
                { 
                    Id = 1, 
                    Title = "Test Story 1", 
                    Description = "Test Description 1",
                    Status = StoryStatus.ToDo,
                    Priority = StoryPriority.Low,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                },
                new Story 
                { 
                    Id = 2, 
                    Title = "Test Story 2", 
                    Description = "Test Description 2",
                    Status = StoryStatus.InProgress,
                    Priority = StoryPriority.High,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _mockStoryService.GetAllStoriesAsync().Returns(expectedStories);

            // Act
            var result = await _controller.GetAllStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<Story>>(okResult.Value);
            Assert.Equal(expectedStories.Count, actualStories.Count);
            Assert.Equal(expectedStories, actualStories);
            await _mockStoryService.Received(1).GetAllStoriesAsync();
        }

        [Fact]
        public async Task GetAllStories_ReturnsOkResult_WithEmptyList_WhenNoStories()
        {
            // Arrange
            var expectedStories = new List<Story>();
            _mockStoryService.GetAllStoriesAsync().Returns(expectedStories);

            // Act
            var result = await _controller.GetAllStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<Story>>(okResult.Value);
            Assert.Empty(actualStories);
            await _mockStoryService.Received(1).GetAllStoriesAsync();
        }





        [Fact]
        public async Task GetDraftStories_ReturnsOkResult_WithDraftStories()
        {
            // Arrange
            var expectedDraftStories = new List<Story>
            {
                new Story 
                { 
                    Id = 1, 
                    Title = "Draft Story 1", 
                    Description = "Draft Description 1",
                    Status = StoryStatus.ToDo,
                    Priority = StoryPriority.Low,
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
            var actualStories = Assert.IsType<List<Story>>(okResult.Value);
            Assert.Single(actualStories);
            Assert.True(actualStories[0].IsDraft);
            await _mockStoryService.Received(1).GetDraftStoriesAsync();
        }

        [Fact]
        public async Task GetDraftStories_ReturnsOkResult_WithEmptyList_WhenNoDraftStories()
        {
            // Arrange
            var expectedStories = new List<Story>();
            _mockStoryService.GetDraftStoriesAsync().Returns(expectedStories);

            // Act
            var result = await _controller.GetDraftStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<Story>>(okResult.Value);
            Assert.Empty(actualStories);
            await _mockStoryService.Received(1).GetDraftStoriesAsync();
        }





        [Fact]
        public async Task GenerateAiStory_ReturnsOkResult_WithGeneratedStories_WhenValidProblemStatements()
        {
            // Arrange
            var problemStatements = new List<string> { "As a user, I want to log in to the system" };
            var expectedStories = new List<Story>
            {
                new Story
                {
                    Id = 1,
                    Title = "User Login Story",
                    Description = "Generated story description",
                    Status = StoryStatus.ToDo,
                    Priority = StoryPriority.Low,
                    IsAiGenerated = true,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _mockStoryService.GenerateAiStory(problemStatements).Returns(expectedStories);

            // Act
            var result = await _controller.GenerateAiStory(problemStatements);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<Story>>(okResult.Value);
            Assert.Equal(expectedStories[0].Id, actualStories[0].Id);
            Assert.Equal(expectedStories[0].Title, actualStories[0].Title);
            Assert.Equal(expectedStories[0].IsAiGenerated, actualStories[0].IsAiGenerated);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatements);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsBadRequest_WhenProblemStatementsIsNullOrEmpty()
        {
            // Act
            var resultNull = await _controller.GenerateAiStory(null!);
            var resultEmpty = await _controller.GenerateAiStory(new List<string>());

            // Assert
            Assert.Equal("At least one problem statement is required.", Assert.IsType<BadRequestObjectResult>(resultNull.Result).Value);
            Assert.Equal("At least one problem statement is required.", Assert.IsType<BadRequestObjectResult>(resultEmpty.Result).Value);
            await _mockStoryService.DidNotReceive().GenerateAiStory(Arg.Any<List<string>>());
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
            var result = await _controller.GenerateAiStory(problemStatements);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal("All problem statements must be non-empty strings.", badRequestResult.Value);
            await _mockStoryService.DidNotReceive().GenerateAiStory(Arg.Any<List<string>>());
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsBadRequest_WhenInvalidOperationExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Invalid operation occurred";
            _mockStoryService.GenerateAiStory(problemStatements)
                .Returns(Task.FromException<List<Story>>(new InvalidOperationException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStory(problemStatements);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal($"Failed to generate AI story: {exceptionMessage}", badRequestResult.Value);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatements);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode502_WhenHttpRequestExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Network error";
            _mockStoryService.GenerateAiStory(problemStatements)
                .Returns(Task.FromException<List<Story>>(new HttpRequestException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStory(problemStatements);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(502, statusCodeResult.StatusCode);
            Assert.Equal($"Failed to communicate with Ollama service: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatements);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode408_WhenTimeoutExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Request timed out";
            _mockStoryService.GenerateAiStory(problemStatements)
                .Returns(Task.FromException<List<Story>>(new TimeoutException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStory(problemStatements);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(408, statusCodeResult.StatusCode);
            Assert.Equal($"Request timed out: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatements);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode500_WhenUnexpectedExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Unexpected error";
            _mockStoryService.GenerateAiStory(problemStatements)
                .Returns(Task.FromException<List<Story>>(new Exception(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStory(problemStatements);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal($"An unexpected error occurred: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatements);
        }





        [Fact]
        public async Task CreateStory_ReturnsOkResult_WithCreatedStory()
        {
            // Arrange
            var inputStory = new Story
            {
                Title = "New Story",
                Description = "New Description",
                Status = StoryStatus.ToDo,
                Priority = StoryPriority.Medium
            };

            var createdStory = new Story
            {
                Id = 1,
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
            var actualStory = Assert.IsType<Story>(okResult.Value);
            Assert.Equal(createdStory.Id, actualStory.Id);
            Assert.Equal(createdStory.Title, actualStory.Title);
            await _mockStoryService.Received(1).CreateStoryAsync(inputStory);
        }





        [Fact]
        public async Task UpdateStory_ReturnsOkResult_WithUpdatedStory()
        {
            // Arrange
            var updatedStory = new Story
            {
                Id = 1,
                Title = "Updated Story",
                Description = "Updated Description",
                Status = StoryStatus.InProgress,
                Priority = StoryPriority.High,
                DateCreated = DateTime.UtcNow.AddDays(-1),
                LastUpdated = DateTime.UtcNow
            };

            _mockStoryService.UpdateStoryAsync(updatedStory).Returns(updatedStory);

            // Act
            var result = await _controller.UpdateStory(updatedStory);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStory = Assert.IsType<Story>(okResult.Value);
            Assert.Equal(updatedStory.Id, actualStory.Id);
            Assert.Equal(updatedStory.Title, actualStory.Title);
            Assert.Equal(StoryStatus.InProgress, actualStory.Status);
            await _mockStoryService.Received(1).UpdateStoryAsync(updatedStory);
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
