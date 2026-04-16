using NSubstitute;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests
{
    public class SprintServiceTests
    {
        private readonly ISprintRepository _mockRepository;
        private readonly SprintService _sprintService;

        public SprintServiceTests()
        {
            _mockRepository = Substitute.For<ISprintRepository>();
            _sprintService = new SprintService(_mockRepository);
        }

        [Fact]
        public async Task GetAllSprintsAsync_ReturnsSprintsFromRepository()
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
            _mockRepository.GetAllSprintsAsync().Returns(expectedSprints);

            // Act
            var result = await _sprintService.GetAllSprintsAsync();

            // Assert
            var actualSprints = result.ToList();
            Assert.Equal(expectedSprints.Count, actualSprints.Count);
            Assert.Equal(expectedSprints, actualSprints);
            await _mockRepository.Received(1).GetAllSprintsAsync();
        }

        [Fact]
        public async Task GetAllSprintsAsync_ReturnsEmptyList_WhenNoSprints()
        {
            // Arrange
            _mockRepository.GetAllSprintsAsync().Returns(new List<Sprint>());

            // Act
            var result = await _sprintService.GetAllSprintsAsync();

            // Assert
            Assert.Empty(result);
            await _mockRepository.Received(1).GetAllSprintsAsync();
        }
    }
}
