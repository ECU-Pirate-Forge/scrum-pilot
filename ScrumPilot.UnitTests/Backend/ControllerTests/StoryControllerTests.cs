using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class PbiControllerTests
    {
        private readonly IPbiService _mockPbiService;
        private readonly PbiController _controller;

        public PbiControllerTests()
        {
            _mockPbiService = Substitute.For<IPbiService>();
            _controller = new PbiController(_mockPbiService);
        }

        [Fact]
        public async Task GetAllPbis_ReturnsOkResult_WithListOfPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>
            {
                new ProductBacklogItem
                {
                    PbiId = 1,
                    Title = "Test PBI 1",
                    Description = "Test Description 1",
                    Status = PbiStatus.ToDo,
                    Priority = PbiPriority.Low,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                },
                new ProductBacklogItem
                {
                    PbiId = 2,
                    Title = "Test PBI 2",
                    Description = "Test Description 2",
                    Status = PbiStatus.InProgress,
                    Priority = PbiPriority.High,
                    DateCreated = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _mockPbiService.GetAllPbisAsync().Returns(expectedPbis);

            // Act
            var result = await _controller.GetAllPbis();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(expectedPbis.Count, actualPbis.Count);
            Assert.Equal(expectedPbis, actualPbis);
            await _mockPbiService.Received(1).GetAllPbisAsync();
        }

        [Fact]
        public async Task GetAllPbis_ReturnsOkResult_WithEmptyList_WhenNoPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>();
            _mockPbiService.GetAllPbisAsync().Returns(expectedPbis);

            // Act
            var result = await _controller.GetAllPbis();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Empty(actualPbis);
            await _mockPbiService.Received(1).GetAllPbisAsync();
        }





        [Fact]
        public async Task GetDraftPbis_ReturnsOkResult_WithDraftPbis()
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

            _mockPbiService.GetDraftPbisAsync().Returns(expectedDraftStories);

            // Act
            var result = await _controller.GetDraftPbis();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualStories);
            Assert.True(actualStories[0].IsDraft);
            await _mockPbiService.Received(1).GetDraftPbisAsync();
        }

        [Fact]
        public async Task GetDraftPbis_ReturnsOkResult_WithEmptyList_WhenNoDraftPbis()
        {
            // Arrange
            var expectedStories = new List<ProductBacklogItem>();
            _mockPbiService.GetDraftPbisAsync().Returns(expectedStories);

            // Act
            var result = await _controller.GetDraftPbis();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Empty(actualStories);
            await _mockPbiService.Received(1).GetDraftPbisAsync();
        }





        [Fact]
        public async Task GenerateAiPbi_ReturnsOkResult_WithGeneratedPbis_WhenValidProblemStatements()
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

            _mockPbiService.GenerateAiPbis(problemStatements).Returns(expectedStories);

            // Act
            var result = await _controller.GenerateAiPbis(problemStatements);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(expectedStories[0].PbiId, actualStories[0].PbiId);
            Assert.Equal(expectedStories[0].Title, actualStories[0].Title);
            Assert.Equal(expectedStories[0].Origin, actualStories[0].Origin);
            await _mockPbiService.Received(1).GenerateAiPbis(problemStatements);
        }

        [Fact]
        public async Task GenerateAiPbi_ReturnsBadRequest_WhenProblemStatementsIsNullOrEmpty()
        {
            // Act
            var resultNull = await _controller.GenerateAiPbis(null!);
            var resultEmpty = await _controller.GenerateAiPbis(new List<string>());

            // Assert
            Assert.Equal("At least one problem statement is required.", Assert.IsType<BadRequestObjectResult>(resultNull.Result).Value);
            Assert.Equal("At least one problem statement is required.", Assert.IsType<BadRequestObjectResult>(resultEmpty.Result).Value);
            await _mockPbiService.DidNotReceive().GenerateAiPbis(Arg.Any<List<string>>());
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(null)]
        public async Task GenerateAiPbi_ReturnsBadRequest_WhenAnyProblemStatementIsNullOrWhitespace(string? invalidStatement)
        {
            // Arrange
            var problemStatements = new List<string> { "Valid statement", invalidStatement! };

            // Act
            var result = await _controller.GenerateAiPbis(problemStatements);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal("All problem statements must be non-empty strings.", badRequestResult.Value);
            await _mockPbiService.DidNotReceive().GenerateAiPbis(Arg.Any<List<string>>());
        }

        [Fact]
        public async Task GenerateAiPbi_ReturnsBadRequest_WhenInvalidOperationExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Invalid operation occurred";
            _mockPbiService.GenerateAiPbis(problemStatements)
                .Returns(Task.FromException<List<ProductBacklogItem>>(new InvalidOperationException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiPbis(problemStatements);
            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal($"Failed to generate AI story: {exceptionMessage}", badRequestResult.Value);
            await _mockPbiService.Received(1).GenerateAiPbis(problemStatements);
        }

        [Fact]
        public async Task GenerateAiPbi_ReturnsStatusCode502_WhenHttpRequestExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Network error";
            _mockPbiService.GenerateAiPbis(problemStatements)
                .Returns(Task.FromException<List<ProductBacklogItem>>(new HttpRequestException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiPbis(problemStatements);
            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(502, statusCodeResult.StatusCode);
            Assert.Equal($"Failed to communicate with Ollama service: {exceptionMessage}", statusCodeResult.Value);
            await _mockPbiService.Received(1).GenerateAiPbis(problemStatements);
        }

        [Fact]
        public async Task GenerateAiPbi_ReturnsStatusCode408_WhenTimeoutExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Request timed out";
            _mockPbiService.GenerateAiPbis(problemStatements)
                .Returns(Task.FromException<List<ProductBacklogItem>>(new TimeoutException(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiPbis(problemStatements);
            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(408, statusCodeResult.StatusCode);
            Assert.Equal($"Request timed out: {exceptionMessage}", statusCodeResult.Value);
            await _mockPbiService.Received(1).GenerateAiPbis(problemStatements);
        }

        [Fact]
        public async Task GenerateAiPbi_ReturnsStatusCode500_WhenUnexpectedExceptionThrown()
        {
            // Arrange
            var problemStatements = new List<string> { "Test problem statement" };
            var exceptionMessage = "Unexpected error";
            _mockPbiService.GenerateAiPbis(problemStatements)
                .Returns(Task.FromException<List<ProductBacklogItem>>(new Exception(exceptionMessage)));

            // Act
            var result = await _controller.GenerateAiPbis(problemStatements);
            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, statusCodeResult.StatusCode);
            Assert.Equal($"An unexpected error occurred: {exceptionMessage}", statusCodeResult.Value);
            await _mockPbiService.Received(1).GenerateAiPbis(problemStatements);
        }

        [Fact]
        public async Task CreatePbi_ReturnsOkResult_WithCreatedPbi()
        {
            // Arrange
            var inputPbi = new ProductBacklogItem
            {
                Title = "New PBI",
                Description = "New Description",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Medium
            };

            var createdPbi = new ProductBacklogItem
            {
                PbiId = 1,
                Title = inputPbi.Title,
                Description = inputPbi.Description,
                Status = inputPbi.Status,
                Priority = inputPbi.Priority,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _mockPbiService.CreatePbiAsync(inputPbi).Returns(createdPbi);

            // Act
            var result = await _controller.CreatePbi(inputPbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(createdPbi.PbiId, actualPbi.PbiId);
            Assert.Equal(createdPbi.Title, actualPbi.Title);
            await _mockPbiService.Received(1).CreatePbiAsync(inputPbi);
        }

        [Fact]
        public async Task UpdatePbi_ReturnsOkResult_WithUpdatedPbi()
        {
            // Arrange
            var updatedPbi = new ProductBacklogItem
            {
                PbiId = 1,
                Title = "Updated PBI",
                Description = "Updated Description",
                Status = PbiStatus.InProgress,
                Priority = PbiPriority.High,
                DateCreated = DateTime.UtcNow.AddDays(-1),
                LastUpdated = DateTime.UtcNow
            };

            _mockPbiService.UpdatePbiAsync(updatedPbi).Returns(updatedPbi);

            // Act
            var result = await _controller.UpdatePbi(updatedPbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(updatedPbi.PbiId, actualPbi.PbiId);
            Assert.Equal(updatedPbi.Title, actualPbi.Title);
            Assert.Equal(PbiStatus.InProgress, actualPbi.Status);
            await _mockPbiService.Received(1).UpdatePbiAsync(updatedPbi);
        }

        [Fact]
        public async Task UpdatePbi_ReturnsOkResult_WhenStatusIsInReview()
        {
            // Arrange - ScrumBoard drag-and-drop can move a card into the InReview lane
            var pbi = new ProductBacklogItem
            {
                PbiId = 2,
                Title = "Story Under Review",
                Description = "In review description",
                Status = PbiStatus.InReview,
                Priority = PbiPriority.Medium,
                DateCreated = DateTime.UtcNow.AddDays(-2),
                LastUpdated = DateTime.UtcNow
            };

            _mockPbiService.UpdatePbiAsync(pbi).Returns(pbi);

            // Act
            var result = await _controller.UpdatePbi(pbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(PbiStatus.InReview, actualPbi.Status);
            await _mockPbiService.Received(1).UpdatePbiAsync(pbi);
        }

        [Fact]
        public async Task UpdatePbi_ReturnsOkResult_WhenPriorityIsNone()
        {
            // Arrange - Backlog drag-and-drop can move a card into the None priority lane
            var pbi = new ProductBacklogItem
            {
                PbiId = 3,
                Title = "Untriaged Story",
                Description = "Priority not yet assigned",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.None,
                DateCreated = DateTime.UtcNow.AddDays(-1),
                LastUpdated = DateTime.UtcNow
            };

            _mockPbiService.UpdatePbiAsync(pbi).Returns(pbi);

            // Act
            var result = await _controller.UpdatePbi(pbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(PbiPriority.None, actualPbi.Priority);
            await _mockPbiService.Received(1).UpdatePbiAsync(pbi);
        }

        [Fact]
        public async Task CommitDraftPbi_ReturnsOkResult_WithCommittedPbi()
        {
            // Arrange
            var draftPbi = new ProductBacklogItem
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

            var committedPbi = new ProductBacklogItem
            {
                PbiId = draftPbi.PbiId,
                Title = draftPbi.Title,
                Description = draftPbi.Description,
                Status = draftPbi.Status,
                Priority = draftPbi.Priority,
                IsDraft = false,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _mockPbiService.CommitPbiAsync(draftPbi).Returns(committedPbi);

            // Act
            var result = await _controller.CommitPbi(draftPbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(committedPbi.PbiId, actualPbi.PbiId);
            Assert.False(actualPbi.IsDraft);
            await _mockPbiService.Received(1).CommitPbiAsync(draftPbi);
        }

        [Fact]
        public async Task CommitDraftPbi_ReturnsNotFound_WhenDraftPbiMissing()
        {
            // Arrange
            var draftPbi = new ProductBacklogItem
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

            _mockPbiService
                .CommitPbiAsync(draftPbi)
                .Returns(Task.FromException<ProductBacklogItem>(new KeyNotFoundException("Draft PBI not found.")));

            // Act
            var result = await _controller.CommitPbi(draftPbi);

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
            Assert.Equal("Draft PBI not found.", notFoundResult.Value);
            await _mockPbiService.Received(1).CommitPbiAsync(draftPbi);
        }

        [Fact]
        public async Task DeletePbi_ReturnsNoContent_WhenSuccessfullyDeleted()
        {
            // Arrange
            var pbiId = 1;
            _mockPbiService.DeletePbiAsync(pbiId).Returns(true);

            // Act
            var result = await _controller.DeletePbi(pbiId);

            // Assert
            Assert.IsType<NoContentResult>(result);
            await _mockPbiService.Received(1).DeletePbiAsync(pbiId);
        }

        [Fact]
        public async Task DeletePbi_ReturnsNotFound_WhenPbiDoesNotExist()
        {
            // Arrange
            var pbiId = 999;
            _mockPbiService.DeletePbiAsync(pbiId).Returns(false);

            // Act
            var result = await _controller.DeletePbi(pbiId);

            // Assert
            Assert.IsType<NotFoundResult>(result);
            await _mockPbiService.Received(1).DeletePbiAsync(pbiId);
        }

        [Fact]
        public async Task GetNonDraftPbis_NoFilters_ReturnsAllNonDraftPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "PBI 1", IsDraft = false },
                new ProductBacklogItem { PbiId = 2, Title = "PBI 2", IsDraft = false }
            };
            _mockPbiService.GetNonDraftPbisAsync().Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(null, null);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(2, actualPbis.Count);
            await _mockPbiService.Received(1).GetNonDraftPbisAsync();
            await _mockPbiService.DidNotReceive().GetFilteredPbisAsync(Arg.Any<int?>(), Arg.Any<int?>());
        }

        [Fact]
        public async Task GetNonDraftPbis_WithSprintId_ReturnsFilteredPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "PBI 1", SprintId = 1 }
            };
            _mockPbiService.GetFilteredPbisAsync(1, null).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(1, null);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualPbis);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(1, null);
            await _mockPbiService.DidNotReceive().GetNonDraftPbisAsync();
        }

        [Fact]
        public async Task GetNonDraftPbis_WithEpicId_ReturnsFilteredPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "PBI 1", EpicId = 2 }
            };
            _mockPbiService.GetFilteredPbisAsync(null, 2).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(null, 2);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualPbis);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(null, 2);
            await _mockPbiService.DidNotReceive().GetNonDraftPbisAsync();
        }

        [Fact]
        public async Task GetNonDraftPbis_WithBothFilters_ReturnsAndFilteredPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "PBI 1", SprintId = 1, EpicId = 2 }
            };
            _mockPbiService.GetFilteredPbisAsync(1, 2).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(1, 2);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualPbis);
            Assert.Equal(1, actualPbis[0].SprintId);
            Assert.Equal(2, actualPbis[0].EpicId);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(1, 2);
        }

        [Fact]
        public async Task GetNonDraftPbis_WithFilters_ReturnsEmptyList_WhenNoMatch()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>();
            _mockPbiService.GetFilteredPbisAsync(99, 99).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(99, 99);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Empty(actualPbis);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(99, 99);
        }
    }
}