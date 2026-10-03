using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ControllerTests;

public sealed class UserSettingsControllerTests
{
    [Fact]
    public async Task UpdateSettings_MapsValidationFailureToSafeBadRequest()
    {
        var service = Substitute.For<IUserSettingsService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns("user");
        service.UpdateSettingsAsync(
                "user",
                Arg.Any<UserSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(UserSettingsUpdateResult.Validation(
                "The selected default project is not available."));
        var controller = new UserController(
            service,
            currentUser,
            Substitute.For<IOrganizationAccessService>());

        var result = await controller.UpdateSettings(new UserSettingsDto());

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("selected default project is not available", json);
        Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateSettings_ReturnsNoContentForSuccessfulClear()
    {
        var service = Substitute.For<IUserSettingsService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns("user");
        service.UpdateSettingsAsync(
                "user",
                Arg.Is<UserSettingsDto>(settings =>
                    settings.DefaultOrganizationId == null
                    && settings.DefaultProjectId == null),
                Arg.Any<CancellationToken>())
            .Returns(UserSettingsUpdateResult.Success);
        var controller = new UserController(
            service,
            currentUser,
            Substitute.For<IOrganizationAccessService>());

        var result = await controller.UpdateSettings(new UserSettingsDto());

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateSettings_MapsTransactionConflictToConflictResponse()
    {
        var service = Substitute.For<IUserSettingsService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns("user");
        service.UpdateSettingsAsync(
                "user",
                Arg.Any<UserSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(UserSettingsUpdateResult.Conflict(
                "Settings changed concurrently. Please reload and try again."));
        var controller = new UserController(
            service,
            currentUser,
            Substitute.For<IOrganizationAccessService>());

        var result = await controller.UpdateSettings(new UserSettingsDto());

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var json = JsonSerializer.Serialize(conflict.Value);
        Assert.Contains("reload and try again", json);
    }
}
