using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class SprintControllerTests
    {
        private readonly ISprintService _mockSprintService;
        private readonly SprintController _controller;

        public SprintControllerTests()
        {
            _mockSprintService = Substitute.For<ISprintService>();
            _controller = new SprintController(_mockSprintService);
        }

        [Fact]
        public async Task GetAllSprints_ReturnsOkResult_WithListOfSprints()
        {
            // Arrange
            var expectedSprints = new List<Sprint>
            {
                new Sprint
                {
                    SprintId = 1,
                    SprintGoal = "Sprint 1 Goal",
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(14),
                    IsOpen = true
                },
                new Sprint
                {
                    SprintId = 2,
                    SprintGoal = "Sprint 2 Goal",
                    StartDate = DateTime.UtcNow.AddDays(14),
                    EndDate = DateTime.UtcNow.AddDays(28),
                    IsOpen = false
                }
            };

            _mockSprintService.GetAllSprintsAsync().Returns(expectedSprints);

            // Act
            var result = await _controller.GetAllSprints();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualSprints = Assert.IsType<List<Sprint>>(okResult.Value);
            Assert.Equal(expectedSprints.Count, actualSprints.Count);
            Assert.Equal(expectedSprints, actualSprints);
            await _mockSprintService.Received(1).GetAllSprintsAsync();
        }

        [Fact]
        public async Task GetAllSprints_ReturnsOkResult_WithEmptyList_WhenNoSprints()
        {
            // Arrange
            var expectedSprints = new List<Sprint>();
            _mockSprintService.GetAllSprintsAsync().Returns(expectedSprints);

            // Act
            var result = await _controller.GetAllSprints();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualSprints = Assert.IsType<List<Sprint>>(okResult.Value);
            Assert.Empty(actualSprints);
            await _mockSprintService.Received(1).GetAllSprintsAsync();
        }
    }
}
