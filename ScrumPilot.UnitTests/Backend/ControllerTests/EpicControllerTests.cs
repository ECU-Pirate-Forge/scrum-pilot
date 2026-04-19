using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class EpicControllerTests
    {
        private readonly IEpicService _mockEpicService;
        private readonly EpicController _controller;

        public EpicControllerTests()
        {
            _mockEpicService = Substitute.For<IEpicService>();
            _controller = new EpicController(_mockEpicService);
        }

        [Fact]
        public async Task GetAllEpics_ReturnsOkResult_WithListOfEpics()
        {
            // Arrange
            var expectedEpics = new List<Epic>
            {
                new Epic
                {
                    EpicId = 1,
                    Name = "Scrum Board Filtering",
                    DateCreated = DateTime.UtcNow
                },
                new Epic
                {
                    EpicId = 2,
                    Name = "User Management",
                    DateCreated = DateTime.UtcNow
                }
            };

            _mockEpicService.GetAllEpicsAsync().Returns(expectedEpics);

            // Act
            var result = await _controller.GetAllEpics();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualEpics = Assert.IsType<List<Epic>>(okResult.Value);
            Assert.Equal(expectedEpics.Count, actualEpics.Count);
            Assert.Equal(expectedEpics, actualEpics);
            await _mockEpicService.Received(1).GetAllEpicsAsync();
        }

        [Fact]
        public async Task GetAllEpics_ReturnsOkResult_WithEmptyList_WhenNoEpics()
        {
            // Arrange
            var expectedEpics = new List<Epic>();
            _mockEpicService.GetAllEpicsAsync().Returns(expectedEpics);

            // Act
            var result = await _controller.GetAllEpics();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var actualEpics = Assert.IsType<List<Epic>>(okResult.Value);
            Assert.Empty(actualEpics);
            await _mockEpicService.Received(1).GetAllEpicsAsync();
        }
    }
}
