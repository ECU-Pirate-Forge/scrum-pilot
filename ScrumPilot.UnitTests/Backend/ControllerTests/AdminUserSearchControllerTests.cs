using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;

namespace ScrumPilot.UnitTests.Backend.ControllerTests;

public sealed class AdminUserSearchControllerTests
{
    [Fact]
    public void Search_is_restricted_to_global_admins()
    {
        var method = typeof(UserController).GetMethod("SearchUsers");
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin", authorize?.Roles);
    }

    [Fact]
    public async Task Search_uses_bounded_service_query()
    {
        var service = Substitute.For<IUserSettingsService>();
        var controller = new UserController(
            service,
            Substitute.For<ICurrentUser>(),
            Substitute.For<IOrganizationAccessService>());

        await controller.SearchUsers("alice", default);

        await service.Received(1).SearchUsersAsync("alice", 20, default);
    }
}
