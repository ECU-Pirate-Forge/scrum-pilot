using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ScrumPilot.API.Authorization;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.Authorization;

public class CurrentUserTests
{
    [Fact]
    public void UserId_ReturnsNameIdentifierForAuthenticatedUser()
    {
        var currentUser = CreateCurrentUser(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "user-1")],
                "Test"));

        Assert.Equal("user-1", currentUser.UserId);
    }

    [Theory]
    [InlineData(false, "user-1")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    public void UserId_ThrowsWhenAuthenticatedIdentifierIsUnavailable(
        bool authenticated,
        string? userId)
    {
        var claims = userId is null
            ? []
            : new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        var currentUser = CreateCurrentUser(
            new ClaimsIdentity(claims, authenticated ? "Test" : null));

        var exception = Assert.Throws<InvalidOperationException>(() => currentUser.UserId);

        Assert.Equal("An authenticated user identifier is required.", exception.Message);
    }

    [Fact]
    public void UserId_ThrowsWhenThereIsNoPrincipal()
    {
        var currentUser = new CurrentUser(new HttpContextAccessor());

        var exception = Assert.Throws<InvalidOperationException>(() => currentUser.UserId);

        Assert.Equal("An authenticated user identifier is required.", exception.Message);
    }

    [Fact]
    public void IsInRole_DelegatesToPrincipal()
    {
        var currentUser = CreateCurrentUser(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, "Admin")],
                "Test"));

        Assert.True(currentUser.IsInRole("Admin"));
        Assert.False(currentUser.IsInRole("Member"));
    }

    [Fact]
    public void IsInRole_ReturnsFalseWithoutPrincipal()
    {
        var currentUser = new CurrentUser(new HttpContextAccessor());

        Assert.False(currentUser.IsInRole("Admin"));
    }

    private static CurrentUser CreateCurrentUser(ClaimsIdentity identity)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        return new CurrentUser(new HttpContextAccessor { HttpContext = context });
    }
}
