using NSubstitute;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests
{
    public class MetricsDashboardServiceTests
    {
        private readonly ISprintRepository _mockSprints;
        private readonly IPbiRepository _mockPbis;
        private readonly IPbiHistoryRepository _mockHistory;
        private readonly MetricsDashboardService _service;

        public MetricsDashboardServiceTests()
        {
            _mockSprints = Substitute.For<ISprintRepository>();
            _mockPbis = Substitute.For<IPbiRepository>();
            _mockHistory = Substitute.For<IPbiHistoryRepository>();
            _service = new MetricsDashboardService(_mockSprints, _mockPbis, _mockHistory);
        }

        private static Sprint MakeSprint(int id, string goal, DateTime start, DateTime end, bool isOpen = true) =>
            new()
            {
                SprintId = id,
                SprintGoal = goal,
                StartDate = start,
                EndDate = end,
                IsOpen = isOpen
            };

        private static ProductBacklogItem MakePbi(int id, PbiStatus status, PbiPoints points = PbiPoints.Five) =>
            new()
            {
                PbiId = id,
                Title = $"PBI {id}",
                Status = status,
                StoryPoints = points,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

        // ── GetSprintSummaryAsync ───────────────────────────────────────────────

        [Fact]
        public async Task GetSprintSummaryAsync_WhenSprintExists_ReturnsSummaryDto()
        {
            // Arrange
            var start = DateTime.UtcNow.Date.AddDays(-3);
            var end = DateTime.UtcNow.Date.AddDays(11);
            var sprint = MakeSprint(1, "Deliver login", start, end);
            _mockSprints.GetAllSprintsAsync().Returns(new List<Sprint> { sprint });

            // Act
            var result = await _service.GetSprintSummaryAsync(1);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Deliver login", result!.Name);
            Assert.Equal(start, result.StartDate);
            Assert.Equal(end, result.EndDate);
            Assert.True(result.DaysLeft >= 0);
            Assert.True(result.TotalDays > 0);
        }

        [Fact]
        public async Task GetSprintSummaryAsync_WhenSprintNotFound_ReturnsNull()
        {
            // Arrange
            _mockSprints.GetAllSprintsAsync().Returns(new List<Sprint>());

            // Act
            var result = await _service.GetSprintSummaryAsync(99);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetSprintSummaryAsync_WhenGoalIsNull_UsesFallbackName()
        {
            // Arrange
            var sprint = new Sprint
            {
                SprintId = 5,
                SprintGoal = null,
                StartDate = DateTime.UtcNow.Date,
                EndDate = DateTime.UtcNow.Date.AddDays(14),
                IsOpen = true
            };
            _mockSprints.GetAllSprintsAsync().Returns(new List<Sprint> { sprint });

            // Act
            var result = await _service.GetSprintSummaryAsync(5);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Sprint 5", result!.Name);
        }

        // ── GetSprintProgressAsync ──────────────────────────────────────────────

        [Fact]
        public async Task GetSprintProgressAsync_CalculatesCommittedAndCompleted()
        {
            // Arrange — 3 items: 2 done, 1 in-progress. Each 5 points.
            var items = new List<ProductBacklogItem>
            {
                MakePbi(1, PbiStatus.Done, PbiPoints.Five),
                MakePbi(2, PbiStatus.Done, PbiPoints.Five),
                MakePbi(3, PbiStatus.InProgress, PbiPoints.Five)
            };
            _mockPbis.GetFilteredPbisAsync(1, null).Returns(items);

            // Act
            var result = await _service.GetSprintProgressAsync(1);

            // Assert
            Assert.Equal(15, result.CommittedPoints);
            Assert.Equal(10, result.CompletedPoints);
            Assert.Equal(3, result.CommittedCount);
            Assert.Equal(2, result.CompletedCount);
        }

        [Fact]
        public async Task GetSprintProgressAsync_WhenNoItems_ReturnsZeroes()
        {
            // Arrange
            _mockPbis.GetFilteredPbisAsync(1, null).Returns(new List<ProductBacklogItem>());

            // Act
            var result = await _service.GetSprintProgressAsync(1);

            // Assert
            Assert.Equal(0, result.CommittedPoints);
            Assert.Equal(0, result.CompletedPoints);
            Assert.Equal(0, result.CommittedCount);
            Assert.Equal(0, result.CompletedCount);
        }

        // ── GetBurndownDataAsync ────────────────────────────────────────────────

        [Fact]
        public async Task GetBurndownDataAsync_WhenSprintNotFound_ReturnsEmptyList()
        {
            // Arrange
            _mockSprints.GetAllSprintsAsync().Returns(new List<Sprint>());

            // Act
            var result = await _service.GetBurndownDataAsync(99);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetBurndownDataAsync_WhenSprintHasNoDates_ReturnsEmptyList()
        {
            // Arrange
            var sprint = new Sprint { SprintId = 1, SprintGoal = "No dates", StartDate = null, EndDate = null };
            _mockSprints.GetAllSprintsAsync().Returns(new List<Sprint> { sprint });

            // Act
            var result = await _service.GetBurndownDataAsync(1);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetBurndownDataAsync_WhenSprintIsValid_ReturnsPoints()
        {
            // Arrange
            var start = DateTime.UtcNow.Date.AddDays(-7);
            var end = DateTime.UtcNow.Date.AddDays(7);
            var sprint = MakeSprint(1, "Sprint 1", start, end);
            _mockSprints.GetAllSprintsAsync().Returns(new List<Sprint> { sprint });
            _mockPbis.GetFilteredPbisAsync(1, null).Returns(new List<ProductBacklogItem>
            {
                MakePbi(1, PbiStatus.Done, PbiPoints.Five),
                MakePbi(2, PbiStatus.InProgress, PbiPoints.Three)
            });
            _mockHistory.GetHistoryForSprintAsync(1).Returns(new List<PbiStatusHistory>());

            // Act
            var result = await _service.GetBurndownDataAsync(1);

            // Assert
            Assert.NotEmpty(result);
            // Every point must have a non-negative ideal value
            Assert.All(result, p => Assert.True(p.Ideal >= 0));
        }

        // ── GetSprintProgressAsync calls correct repository method ─────────────

        [Fact]
        public async Task GetSprintProgressAsync_CallsGetFilteredPbisWithCorrectSprintId()
        {
            // Arrange
            _mockPbis.GetFilteredPbisAsync(3, null).Returns(new List<ProductBacklogItem>());

            // Act
            await _service.GetSprintProgressAsync(3);

            // Assert
            await _mockPbis.Received(1).GetFilteredPbisAsync(3, null);
        }
    }
}
