using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;

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

    [Fact]
    public async Task Service_search_is_case_insensitive_requires_two_characters_and_caps_results()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ScrumPilotContext(
            new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Users.AddRange(Enumerable.Range(1, 25).Select(index => new ApplicationUser
        {
            Id = $"user-{index}",
            UserName = $"Alice{index:00}",
            NormalizedUserName = $"ALICE{index:00}",
            Email = $"alice{index:00}@example.com",
            NormalizedEmail = $"ALICE{index:00}@EXAMPLE.COM"
        }));
        await context.SaveChangesAsync();
        var userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null, null, null, null, null, null, null, null);
        var service = new UserSettingsService(userManager, context);

        var tooShort = await service.SearchUsersAsync("a", 100);
        var matches = await service.SearchUsersAsync(" aLiCe ", 100);

        Assert.Empty(tooShort);
        Assert.Equal(20, matches.Count);
        Assert.All(matches, user =>
            Assert.Contains("Alice", user.UserName, StringComparison.OrdinalIgnoreCase));
    }
}
