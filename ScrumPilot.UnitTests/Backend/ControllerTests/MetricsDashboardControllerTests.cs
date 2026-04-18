using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class MetricsDashboardControllerTests
    {
        private readonly IMetricsDashboardService _mockService;
        private readonly MetricsDashboardController _controller;

        public MetricsDashboardControllerTests()
        {
            _mockService = Substitute.For<IMetricsDashboardService>();
            _controller = new MetricsDashboardController(_mockService);
        }

        // ── GetSprintSummary ────────────────────────────────────────────────────

        [Fact]
        public async Task GetSprintSummary_WhenSprintExists_ReturnsOk()
        {
            // Arrange
            var dto = new SprintSummaryDto("Sprint 1", DateTime.UtcNow, DateTime.UtcNow.AddDays(14), 10, 14);
            _mockService.GetSprintSummaryAsync(1).Returns(dto);

            // Act
            var result = await _controller.GetSprintSummary(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(dto, ok.Value);
            await _mockService.Received(1).GetSprintSummaryAsync(1);
        }

        [Fact]
        public async Task GetSprintSummary_WhenSprintNotFound_ReturnsNotFound()
        {
            // Arrange
            _mockService.GetSprintSummaryAsync(99).Returns((SprintSummaryDto?)null);

            // Act
            var result = await _controller.GetSprintSummary(99);

            // Assert
            Assert.IsType<NotFoundResult>(result.Result);
        }

        // ── GetSprintProgress ───────────────────────────────────────────────────

        [Fact]
        public async Task GetSprintProgress_ReturnsOk_WithProgressData()
        {
            // Arrange
            var dto = new SprintProgressDto(40, 25, 8, 5);
            _mockService.GetSprintProgressAsync(1).Returns(dto);

            // Act
            var result = await _controller.GetSprintProgress(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(dto, ok.Value);
            await _mockService.Received(1).GetSprintProgressAsync(1);
        }

        // ── GetBurndown ─────────────────────────────────────────────────────────

        [Fact]
        public async Task GetBurndown_ReturnsOk_WithBurndownPoints()
        {
            // Arrange
            var points = new List<BurndownPoint>
            {
                new(DateTime.UtcNow, 40, 40),
                new(DateTime.UtcNow.AddDays(1), 35, 37.1)
            };
            _mockService.GetBurndownDataAsync(1).Returns(points);

            // Act
            var result = await _controller.GetBurndown(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<List<BurndownPoint>>(ok.Value);
            Assert.Equal(2, actual.Count);
            await _mockService.Received(1).GetBurndownDataAsync(1);
        }

        [Fact]
        public async Task GetBurndown_ReturnsOk_WithEmptyList_WhenNoData()
        {
            // Arrange
            _mockService.GetBurndownDataAsync(1).Returns(new List<BurndownPoint>());

            // Act
            var result = await _controller.GetBurndown(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<List<BurndownPoint>>(ok.Value);
            Assert.Empty(actual);
        }

        // ── GetVelocity ─────────────────────────────────────────────────────────

        [Fact]
        public async Task GetVelocity_NoSprintFilter_ReturnsAllVelocityPoints()
        {
            // Arrange
            var points = new List<VelocityPoint>
            {
                new("Sprint 1", 30, 28),
                new("Sprint 2", 35, 35)
            };
            _mockService.GetVelocityDataAsync(null).Returns(points);

            // Act
            var result = await _controller.GetVelocity(null);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<List<VelocityPoint>>(ok.Value);
            Assert.Equal(2, actual.Count);
            await _mockService.Received(1).GetVelocityDataAsync(null);
        }

        [Fact]
        public async Task GetVelocity_WithSprintFilter_PassesSprintIdToService()
        {
            // Arrange
            var points = new List<VelocityPoint> { new("Sprint 2", 35, 35) };
            _mockService.GetVelocityDataAsync(2).Returns(points);

            // Act
            var result = await _controller.GetVelocity(2);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            await _mockService.Received(1).GetVelocityDataAsync(2);
        }

        // ── GetWip ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task GetWip_ReturnsOk_WithWipItems()
        {
            // Arrange
            var items = new List<WipItem>
            {
                new(1, "Login feature", "Feature", "High", "InProgress"),
                new(2, "Fix crash", "Bug", "Medium", "InReview")
            };
            _mockService.GetWipItemsAsync(1).Returns(items);

            // Act
            var result = await _controller.GetWip(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<List<WipItem>>(ok.Value);
            Assert.Equal(2, actual.Count);
            await _mockService.Received(1).GetWipItemsAsync(1);
        }

        // ── GetBugTrend ─────────────────────────────────────────────────────────

        [Fact]
        public async Task GetBugTrend_ReturnsOk_WithBugTrendPoints()
        {
            // Arrange
            var points = new List<BugTrendPoint>
            {
                new(DateTime.UtcNow, 3, 1),
                new(DateTime.UtcNow.AddDays(1), 2, 2)
            };
            _mockService.GetBugTrendAsync(1).Returns(points);

            // Act
            var result = await _controller.GetBugTrend(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<List<BugTrendPoint>>(ok.Value);
            Assert.Equal(2, actual.Count);
            await _mockService.Received(1).GetBugTrendAsync(1);
        }

        // ── GetCycleTime ────────────────────────────────────────────────────────

        [Fact]
        public async Task GetCycleTime_ReturnsOk_WithCycleTimePoints()
        {
            // Arrange
            var points = new List<CycleTimePoint>
            {
                new(DateTime.UtcNow, 2.5),
                new(DateTime.UtcNow.AddDays(1), 3.1)
            };
            _mockService.GetCycleTimeDataAsync(1).Returns(points);

            // Act
            var result = await _controller.GetCycleTime(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<List<CycleTimePoint>>(ok.Value);
            Assert.Equal(2, actual.Count);
            await _mockService.Received(1).GetCycleTimeDataAsync(1);
        }

        // ── GetWorkByStatus ─────────────────────────────────────────────────────

        [Fact]
        public async Task GetWorkByStatus_ReturnsOk_WithWorkByStatusPoints()
        {
            // Arrange
            var points = new List<WorkByStatusPoint>
            {
                new("ToDo", 5, 2),
                new("InProgress", 3, 1)
            };
            _mockService.GetWorkByStatusAsync(1).Returns(points);

            // Act
            var result = await _controller.GetWorkByStatus(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<List<WorkByStatusPoint>>(ok.Value);
            Assert.Equal(2, actual.Count);
            await _mockService.Received(1).GetWorkByStatusAsync(1);
        }

        // ── GetTimeInStage ──────────────────────────────────────────────────────

        [Fact]
        public async Task GetTimeInStage_ReturnsOk_WithTimeInStageData()
        {
            // Arrange
            var data = new TimeInStageData
            {
                Stages = ["ToDo", "InProgress", "Done"],
                Points = [new TimeInStagePoint("0-1d", "ToDo", 3)]
            };
            _mockService.GetTimeInStageDataAsync(1).Returns(data);

            // Act
            var result = await _controller.GetTimeInStage(1);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<TimeInStageData>(ok.Value);
            Assert.Equal(3, actual.Stages.Count);
            Assert.Single(actual.Points);
            await _mockService.Received(1).GetTimeInStageDataAsync(1);
        }
    }
}
