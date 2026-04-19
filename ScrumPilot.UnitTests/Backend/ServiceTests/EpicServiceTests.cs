using NSubstitute;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests
{
    public class EpicServiceTests
    {
        private readonly IEpicRepository _mockRepository;
        private readonly EpicService _epicService;

        public EpicServiceTests()
        {
            _mockRepository = Substitute.For<IEpicRepository>();
            _epicService = new EpicService(_mockRepository);
        }

        [Fact]
        public async Task GetAllEpicsAsync_ReturnsEpicsFromRepository()
        {
            // Arrange
            var expectedEpics = new List<Epic>
            {
                new Epic { EpicId = 1, Name = "Scrum Board Filtering", DateCreated = DateTime.UtcNow },
                new Epic { EpicId = 2, Name = "User Management", DateCreated = DateTime.UtcNow }
            };
            _mockRepository.GetAllEpicsAsync().Returns(expectedEpics);

            // Act
            var result = await _epicService.GetAllEpicsAsync();

            // Assert
            var actualEpics = result.ToList();
            Assert.Equal(expectedEpics.Count, actualEpics.Count);
            Assert.Equal(expectedEpics, actualEpics);
            await _mockRepository.Received(1).GetAllEpicsAsync();
        }

        [Fact]
        public async Task GetAllEpicsAsync_ReturnsEmptyList_WhenNoEpics()
        {
            // Arrange
            _mockRepository.GetAllEpicsAsync().Returns(new List<Epic>());

            // Act
            var result = await _epicService.GetAllEpicsAsync();

            // Assert
            Assert.Empty(result);
            await _mockRepository.Received(1).GetAllEpicsAsync();
        }
    }
}
