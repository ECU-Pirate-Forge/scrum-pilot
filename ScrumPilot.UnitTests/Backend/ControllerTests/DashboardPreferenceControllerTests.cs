using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ControllerTests
{
    public class DashboardPreferenceControllerTests
    {
        private readonly IDashboardPreferenceService _mockService;
        private readonly DashboardPreferenceController _controller;
        private const int ProjectId = 1;

        public DashboardPreferenceControllerTests()
        {
            _mockService = Substitute.For<IDashboardPreferenceService>();
            _controller = new DashboardPreferenceController(_mockService);
        }

        private static ControllerContext MakeControllerContext(string? userId)
        {
            var claims = userId is not null
                ? new[] { new Claim(ClaimTypes.NameIdentifier, userId) }
                : Array.Empty<Claim>();

            var identity = new ClaimsIdentity(claims, userId is not null ? "Bearer" : null);
            var principal = new ClaimsPrincipal(identity);

            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
        }

        // ── GET ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Get_WhenAuthenticated_ReturnsOkWithPreferences()
        {
            // Arrange
            _controller.ControllerContext = MakeControllerContext("user-1");
            var dto = new DashboardPreferenceDto
            {
                Widgets =
                [
                    new DashboardWidgetConfig { Id = "burndown", Visible = true, X = 0, Y = 0, W = 6, H = 4 }
                ]
            };
            _mockService.GetPreferencesAsync("user-1", ProjectId).Returns(dto);

            // Act
            var result = await _controller.Get(ProjectId);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<DashboardPreferenceDto>(ok.Value);
            Assert.Single(actual.Widgets);
            Assert.Equal("burndown", actual.Widgets[0].Id);
            await _mockService.Received(1).GetPreferencesAsync("user-1", ProjectId);
        }

        [Fact]
        public async Task Get_WhenUnauthenticated_ReturnsUnauthorized()
        {
            // Arrange — no user identity
            _controller.ControllerContext = MakeControllerContext(null);

            // Act
            var result = await _controller.Get(ProjectId);

            // Assert
            Assert.IsType<UnauthorizedResult>(result.Result);
            await _mockService.DidNotReceive().GetPreferencesAsync(Arg.Any<string>(), Arg.Any<int>());
        }

        [Fact]
        public async Task Get_WhenNoWidgetsSaved_ReturnsOkWithEmptyWidgets()
        {
            // Arrange
            _controller.ControllerContext = MakeControllerContext("user-2");
            _mockService.GetPreferencesAsync("user-2", ProjectId).Returns(new DashboardPreferenceDto());

            // Act
            var result = await _controller.Get(ProjectId);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var actual = Assert.IsType<DashboardPreferenceDto>(ok.Value);
            Assert.Empty(actual.Widgets);
        }

        // ── PUT ─────────────────────────────────────────────────────────────────

        [Fact]
        public async Task Put_WhenAuthenticated_ReturnsNoContent()
        {
            // Arrange
            _controller.ControllerContext = MakeControllerContext("user-1");
            var dto = new DashboardPreferenceDto
            {
                Widgets =
                [
                    new DashboardWidgetConfig { Id = "velocity", Visible = false, X = 6, Y = 0, W = 6, H = 4 }
                ]
            };

            // Act
            var result = await _controller.Put(dto, ProjectId);

            // Assert
            Assert.IsType<NoContentResult>(result);
            await _mockService.Received(1).SavePreferencesAsync("user-1", ProjectId, dto);
        }

        [Fact]
        public async Task Put_WhenUnauthenticated_ReturnsUnauthorized()
        {
            // Arrange — no user identity
            _controller.ControllerContext = MakeControllerContext(null);
            var dto = new DashboardPreferenceDto();

            // Act
            var result = await _controller.Put(dto, ProjectId);

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
            await _mockService.DidNotReceive().SavePreferencesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DashboardPreferenceDto>());
        }

        [Fact]
        public async Task Put_SavesCorrectUserIdAndDto()
        {
            // Arrange
            _controller.ControllerContext = MakeControllerContext("user-3");
            var dto = new DashboardPreferenceDto
            {
                Widgets =
                [
                    new DashboardWidgetConfig { Id = "burndown", Visible = true },
                    new DashboardWidgetConfig { Id = "wip", Visible = false }
                ]
            };

            // Act
            await _controller.Put(dto, ProjectId);

            // Assert
            await _mockService.Received(1).SavePreferencesAsync(
                "user-3",
                ProjectId,
                Arg.Is<DashboardPreferenceDto>(d => d.Widgets.Count == 2));
        }
    }
}
