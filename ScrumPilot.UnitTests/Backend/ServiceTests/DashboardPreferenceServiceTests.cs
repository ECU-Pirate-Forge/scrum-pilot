using NSubstitute;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using System.Text.Json;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests
{
    public class DashboardPreferenceServiceTests
    {
        private readonly IDashboardPreferenceRepository _mockRepo;
        private readonly DashboardPreferenceService _service;
        private const int ProjectId = 1;

        public DashboardPreferenceServiceTests()
        {
            _mockRepo = Substitute.For<IDashboardPreferenceRepository>();
            _service = new DashboardPreferenceService(_mockRepo);
        }

        // ── GetPreferencesAsync ─────────────────────────────────────────────────

        [Fact]
        public async Task GetPreferencesAsync_WhenJsonExists_ReturnsDeserializedDto()
        {
            // Arrange
            var dto = new DashboardPreferenceDto
            {
                Widgets =
                [
                    new DashboardWidgetConfig { Id = "burndown", Visible = true, X = 0, Y = 0, W = 6, H = 4 },
                    new DashboardWidgetConfig { Id = "velocity", Visible = false, X = 6, Y = 0, W = 6, H = 4 }
                ]
            };
            var json = JsonSerializer.Serialize(dto);
            _mockRepo.GetPreferencesJsonAsync("user-1", ProjectId).Returns(json);

            // Act
            var result = await _service.GetPreferencesAsync("user-1", ProjectId);

            // Assert
            Assert.Equal(2, result.Widgets.Count);
            Assert.Equal("burndown", result.Widgets[0].Id);
            Assert.True(result.Widgets[0].Visible);
            Assert.Equal("velocity", result.Widgets[1].Id);
            Assert.False(result.Widgets[1].Visible);
            await _mockRepo.Received(1).GetPreferencesJsonAsync("user-1", ProjectId);
        }

        [Fact]
        public async Task GetPreferencesAsync_WhenJsonIsNull_ReturnsEmptyDto()
        {
            // Arrange
            _mockRepo.GetPreferencesJsonAsync("user-2", ProjectId).Returns((string?)null);

            // Act
            var result = await _service.GetPreferencesAsync("user-2", ProjectId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Widgets);
        }

        [Fact]
        public async Task GetPreferencesAsync_WhenJsonIsEmpty_ReturnsEmptyDto()
        {
            // Arrange
            _mockRepo.GetPreferencesJsonAsync("user-3", ProjectId).Returns(string.Empty);

            // Act
            var result = await _service.GetPreferencesAsync("user-3", ProjectId);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Widgets);
        }

        // ── SavePreferencesAsync ────────────────────────────────────────────────

        [Fact]
        public async Task SavePreferencesAsync_SerializesDtoAndCallsRepository()
        {
            // Arrange
            var dto = new DashboardPreferenceDto
            {
                Widgets = [new DashboardWidgetConfig { Id = "wip", Visible = true }]
            };

            // Act
            await _service.SavePreferencesAsync("user-1", ProjectId, dto);

            // Assert — repo must be called with the correct userId, projectId, and valid JSON
            await _mockRepo.Received(1).UpsertPreferencesJsonAsync(
                "user-1",
                ProjectId,
                Arg.Is<string>(json =>
                    json.Contains("\"Id\":\"wip\"") || json.Contains("\"id\":\"wip\"")));
        }

        [Fact]
        public async Task SavePreferencesAsync_ThenGetPreferencesAsync_RoundTripsCorrectly()
        {
            // Arrange
            var dto = new DashboardPreferenceDto
            {
                Widgets =
                [
                    new DashboardWidgetConfig { Id = "burndown", Visible = true, X = 0, Y = 0, W = 6, H = 4 }
                ]
            };
            string? capturedJson = null;
            await _mockRepo.UpsertPreferencesJsonAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Do<string>(j => capturedJson = j));
            _mockRepo.GetPreferencesJsonAsync("user-1", ProjectId).Returns(_ => capturedJson!);

            // Act — save then load
            await _service.SavePreferencesAsync("user-1", ProjectId, dto);
            var result = await _service.GetPreferencesAsync("user-1", ProjectId);

            // Assert
            Assert.Single(result.Widgets);
            Assert.Equal("burndown", result.Widgets[0].Id);
            Assert.Equal(6, result.Widgets[0].W);
        }
    }
}
