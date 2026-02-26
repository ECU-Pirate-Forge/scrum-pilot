using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.Controller_Tests
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
        public void GetStories_ReturnsOkResult_WithListOfStories()
        {
            // Arrange
            var expectedStories = new List<Story>
            {
                new Story 
                { 
                    Id = Guid.NewGuid(), 
                    Title = "Test Story 1", 
                    Description = "Test Description 1",
                    Status = StoryStatus.ToDo,
                    Priority = StoryPriority.Low,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                },
                new Story 
                { 
                    Id = Guid.NewGuid(), 
                    Title = "Test Story 2", 
                    Description = "Test Description 2",
                    Status = StoryStatus.InProgress,
                    Priority = StoryPriority.High,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _mockStoryService.GetStories().Returns(expectedStories);

            // Act
            var result = _controller.GetStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<Story>>(okResult.Value);
            Assert.Equal(expectedStories.Count, actualStories.Count);
            Assert.Equal(expectedStories, actualStories);
            _mockStoryService.Received(1).GetStories();
        }

        [Fact]
        public void GetStories_ReturnsOkResult_WithEmptyList_WhenNoStories()
        {
            // Arrange
            var expectedStories = new List<Story>();
            _mockStoryService.GetStories().Returns(expectedStories);

            // Act
            var result = _controller.GetStories();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<Story>>(okResult.Value);
            Assert.Empty(actualStories);
            _mockStoryService.Received(1).GetStories();
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsOkResult_WithGeneratedStory_WhenValidProblemStatement()
        {
            // Arrange
            var problemStatement = "As a user, I want to log in to the system";
            var expectedStory = new Story
            {
                Id = Guid.NewGuid(),
                Title = "User Login Story",
                Description = "Generated story description",
                Status = StoryStatus.ToDo,
                Priority = StoryPriority.Low,
                IsAiGenerated = true,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _mockStoryService.GenerateAiStory(problemStatement).Returns(expectedStory);

            // Act
            var result = await _controller.GenerateAiStory(problemStatement);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStory = Assert.IsType<Story>(okResult.Value);
            Assert.Equal(expectedStory.Id, actualStory.Id);
            Assert.Equal(expectedStory.Title, actualStory.Title);
            Assert.Equal(expectedStory.IsAiGenerated, actualStory.IsAiGenerated);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatement);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public async Task GenerateAiStory_ReturnsBadRequest_WhenProblemStatementIsNullOrWhitespace(string problemStatement)
        {
            // Act
            var result = await _controller.GenerateAiStory(problemStatement);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal("Problem statement is required.", badRequestResult.Value);
            await _mockStoryService.DidNotReceive().GenerateAiStory(Arg.Any<string>());
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsBadRequest_WhenInvalidOperationExceptionThrown()
        {
            // Arrange
            var problemStatement = "Test problem statement";
            var exceptionMessage = "Invalid operation occurred";
            _mockStoryService.GenerateAiStory(problemStatement)
                .Returns(Task.FromException<Story>(new InvalidOperationException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStory(problemStatement);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal($"Failed to generate AI story: {exceptionMessage}", badRequestResult.Value);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatement);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode502_WhenHttpRequestExceptionThrown()
        {
            // Arrange
            var problemStatement = "Test problem statement";
            var exceptionMessage = "Network error";
            _mockStoryService.GenerateAiStory(problemStatement)
                .Returns(Task.FromException<Story>(new HttpRequestException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStory(problemStatement);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(502, statusCodeResult.StatusCode);
            Assert.Equal($"Failed to communicate with Ollama service: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatement);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode408_WhenTimeoutExceptionThrown()
        {
            // Arrange
            var problemStatement = "Test problem statement";
            var exceptionMessage = "Request timed out";
            _mockStoryService.GenerateAiStory(problemStatement)
                .Returns(Task.FromException<Story>(new TimeoutException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStory(problemStatement);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(408, statusCodeResult.StatusCode);
            Assert.Equal($"Request timed out: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatement);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStatusCode500_WhenUnexpectedExceptionThrown()
        {
            // Arrange
            var problemStatement = "Test problem statement";
            var exceptionMessage = "Unexpected error";
            _mockStoryService.GenerateAiStory(problemStatement)
                .Returns(Task.FromException<Story>(new Exception(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiStory(problemStatement);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal($"An unexpected error occurred: {exceptionMessage}", statusCodeResult.Value);
            await _mockStoryService.Received(1).GenerateAiStory(problemStatement);
        }
    }
}