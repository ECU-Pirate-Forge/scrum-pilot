using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class PbiControllerTests
    {
        private readonly IPbiService _mockPbiService;
        private readonly IPbiRepository _repository;
        private readonly ICurrentUser _currentUser;
        private readonly IOrganizationAccessService _access;
        private readonly PbiController _controller;

        public PbiControllerTests()
        {
            _mockPbiService = Substitute.For<IPbiService>();
            _repository = Substitute.For<IPbiRepository>();
            _currentUser = Substitute.For<ICurrentUser>();
            _access = Substitute.For<IOrganizationAccessService>();
            _currentUser.UserId.Returns("test-user");
            _access.CanAccessProjectAsync("test-user", 1, Arg.Any<CancellationToken>()).Returns(true);
            _access.SprintBelongsToProjectAsync(Arg.Any<int>(), 1, Arg.Any<CancellationToken>()).Returns(true);
            _access.EpicBelongsToProjectAsync(Arg.Any<int>(), 1, Arg.Any<CancellationToken>()).Returns(true);
            _access.UserCanBeAssignedToProjectAsync(Arg.Any<string>(), 1, Arg.Any<CancellationToken>()).Returns(true);
            _access.PbiBelongsToProjectAsync(Arg.Any<int>(), 1, Arg.Any<CancellationToken>()).Returns(true);
            _repository.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => new ProductBacklogItem
                {
                    PbiId = call.ArgAt<int>(0), ProjectId = 1, Title = "Existing"
                });
            _mockPbiService.CreatePbiAsync(Arg.Any<ProductBacklogItem>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<ProductBacklogItem>(0));
            _mockPbiService.CreateDraftPbiAsync(Arg.Any<ProductBacklogItem>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<ProductBacklogItem>(0));
            _mockPbiService.UpdatePbiAsync(Arg.Any<ProductBacklogItem>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<ProductBacklogItem>(0));
            _mockPbiService.CommitPbiAsync(Arg.Any<ProductBacklogItem>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<ProductBacklogItem>(0));
            _mockPbiService.CreatePbisAsync(
                    Arg.Any<IEnumerable<ProductBacklogItem>>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<IEnumerable<ProductBacklogItem>>(0).ToList());
            _controller = new PbiController(_mockPbiService, _repository, _currentUser, _access);
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

            _repository.GetByProjectAsync(1).Returns(expectedPbis);

            // Act
            var result = await _controller.GetAllPbis(1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(expectedPbis.Count, actualPbis.Count);
            Assert.Equal(expectedPbis, actualPbis);
            await _repository.Received(1).GetByProjectAsync(1);
        }

        [Fact]
        public async Task GetAllPbis_ReturnsOkResult_WithEmptyList_WhenNoPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>();
            _repository.GetByProjectAsync(1).Returns(expectedPbis);

            // Act
            var result = await _controller.GetAllPbis(1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Empty(actualPbis);
            await _repository.Received(1).GetByProjectAsync(1);
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

            _repository.GetByProjectAsync(1, true).Returns(expectedDraftStories);

            // Act
            var result = await _controller.GetDraftPbis(1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualStories);
            Assert.True(actualStories[0].IsDraft);
            await _repository.Received(1).GetByProjectAsync(1, true);
        }

        [Fact]
        public async Task GetDraftPbis_ReturnsOkResult_WithEmptyList_WhenNoDraftPbis()
        {
            // Arrange
            var expectedStories = new List<ProductBacklogItem>();
            _repository.GetByProjectAsync(1, true).Returns(expectedStories);

            // Act
            var result = await _controller.GetDraftPbis(1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualStories = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Empty(actualStories);
            await _repository.Received(1).GetByProjectAsync(1, true);
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
                },
                new ProductBacklogItem
                {
                    PbiId = 2,
                    Title = "Password Recovery Story",
                    Description = "Second generated story description",
                    Status = PbiStatus.ToDo,
                    Priority = PbiPriority.Medium,
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
            Assert.Equal(2, actualStories.Count);
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
            Assert.Equal($"Failed to generate AI PBIs: {exceptionMessage}", badRequestResult.Value);
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
            Assert.Equal($"Failed to communicate with AI service: {exceptionMessage}", statusCodeResult.Value);
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

            _mockPbiService.CreatePbiAsync(
                Arg.Any<ProductBacklogItem>(), Arg.Any<CancellationToken>()).Returns(createdPbi);

            // Act
            var result = await _controller.CreatePbi(1, inputPbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(createdPbi.PbiId, actualPbi.PbiId);
            Assert.Equal(createdPbi.Title, actualPbi.Title);
            await _mockPbiService.Received(1).CreatePbiAsync(
                Arg.Is<ProductBacklogItem>(p => p.ProjectId == 1 && p.Title == inputPbi.Title),
                Arg.Any<CancellationToken>());
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

            // Act
            var result = await _controller.UpdatePbi(updatedPbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(updatedPbi.PbiId, actualPbi.PbiId);
            Assert.Equal(updatedPbi.Title, actualPbi.Title);
            Assert.Equal(PbiStatus.InProgress, actualPbi.Status);
            await _mockPbiService.Received(1).UpdatePbiAsync(
                Arg.Is<ProductBacklogItem>(p => p.PbiId == 1 && p.Title == updatedPbi.Title),
                Arg.Any<CancellationToken>());
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

            // Act
            var result = await _controller.UpdatePbi(pbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(PbiStatus.InReview, actualPbi.Status);
            await _mockPbiService.Received(1).UpdatePbiAsync(
                Arg.Is<ProductBacklogItem>(item => item.PbiId == 2 && item.Status == PbiStatus.InReview),
                Arg.Any<CancellationToken>());
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

            // Act
            var result = await _controller.UpdatePbi(pbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(PbiPriority.None, actualPbi.Priority);
            await _mockPbiService.Received(1).UpdatePbiAsync(
                Arg.Is<ProductBacklogItem>(item => item.PbiId == 3 && item.Priority == PbiPriority.None),
                Arg.Any<CancellationToken>());
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

            // Act
            var result = await _controller.CommitPbi(draftPbi);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbi = Assert.IsType<ProductBacklogItem>(okResult.Value);
            Assert.Equal(committedPbi.PbiId, actualPbi.PbiId);
            Assert.False(actualPbi.IsDraft);
            await _mockPbiService.Received(1).CommitPbiAsync(
                Arg.Is<ProductBacklogItem>(p => p.PbiId == 7 && !p.IsDraft),
                Arg.Any<CancellationToken>());
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

            _repository.GetByIdAsync(99, Arg.Any<CancellationToken>())
                .Returns((ProductBacklogItem?)null);

            // Act
            var result = await _controller.CommitPbi(draftPbi);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
            await _mockPbiService.DidNotReceive().CommitPbiAsync(
                Arg.Any<ProductBacklogItem>(), Arg.Any<CancellationToken>());
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
            _mockPbiService.GetFilteredPbisAsync(null, null, 1).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(2, actualPbis.Count);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(null, null, 1);
        }

        [Fact]
        public async Task GetNonDraftPbis_WithSprintId_ReturnsFilteredPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "PBI 1", SprintId = 1 }
            };
            _mockPbiService.GetFilteredPbisAsync(1, null, 1).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(1, 1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualPbis);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(1, null, 1);
        }

        [Fact]
        public async Task GetNonDraftPbis_WithEpicId_ReturnsFilteredPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "PBI 1", EpicId = 2 }
            };
            _mockPbiService.GetFilteredPbisAsync(null, 2, 1).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(1, null, 2);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualPbis);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(null, 2, 1);
        }

        [Fact]
        public async Task GetNonDraftPbis_WithBothFilters_ReturnsAndFilteredPbis()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "PBI 1", SprintId = 1, EpicId = 2 }
            };
            _mockPbiService.GetFilteredPbisAsync(1, 2, 1).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(1, 1, 2);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Single(actualPbis);
            Assert.Equal(1, actualPbis[0].SprintId);
            Assert.Equal(2, actualPbis[0].EpicId);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(1, 2, 1);
        }

        [Fact]
        public async Task GetNonDraftPbis_WithFilters_ReturnsEmptyList_WhenNoMatch()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>();
            _mockPbiService.GetFilteredPbisAsync(99, 99, 1).Returns(expectedPbis);

            // Act
            var result = await _controller.GetNonDraftPbis(1, 99, 99);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualPbis = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Empty(actualPbis);
            await _mockPbiService.Received(1).GetFilteredPbisAsync(99, 99, 1);
        }

        [Fact]
        public async Task CreatePbis_PersistsEveryItemAsAiGeneratedBacklogPbi()
        {
            var pbis = new List<ProductBacklogItem>
            {
                new() { Title = "First" },
                new() { Title = "Second" }
            };
            var result = await _controller.CreatePbis(1, pbis);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var created = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(2, created.Count);
            Assert.All(created, pbi => Assert.Equal(PbiOrigin.AiGenerated, pbi.Origin));
            await _mockPbiService.Received(1).CreatePbisAsync(
                Arg.Any<IEnumerable<ProductBacklogItem>>(), false, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CreateDraftPbis_PersistsEveryItemAsAiGeneratedDraftPbi()
        {
            var pbis = new List<ProductBacklogItem>
            {
                new() { Title = "First" },
                new() { Title = "Second" }
            };
            var result = await _controller.CreateDraftPbis(1, pbis);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var created = Assert.IsType<List<ProductBacklogItem>>(okResult.Value);
            Assert.Equal(2, created.Count);
            Assert.All(created, pbi => Assert.Equal(PbiOrigin.AiGenerated, pbi.Origin));
            await _mockPbiService.Received(1).CreatePbisAsync(
                Arg.Any<IEnumerable<ProductBacklogItem>>(), true, Arg.Any<CancellationToken>());
            await _mockPbiService.DidNotReceive().CreatePbiAsync(Arg.Any<ProductBacklogItem>());
        }
    }
}