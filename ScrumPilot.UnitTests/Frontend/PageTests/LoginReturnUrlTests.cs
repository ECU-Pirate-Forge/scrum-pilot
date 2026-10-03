using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Pages;
using ScrumPilot.Web.Services;

namespace ScrumPilot.UnitTests.Frontend.PageTests;

public sealed class LoginReturnUrlTests : FrontendTestBase
{
    [Theory]
    [InlineData("/accept-invitation?token=value", "/accept-invitation?token=value")]
    [InlineData("//evil.example/path", "/")]
    [InlineData("https://evil.example/path", "/")]
    public async Task Successful_login_honors_only_safe_local_return_urls(string returnUrl, string expected)
    {
        AuthService.LoginAsync(Arg.Any<LoginRequest>()).Returns(true);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        var cut = Render<Login>();

        var inputs = cut.FindAll("input");
        inputs[0].Change("alice");
        inputs[1].Change("password");
        await cut.Find("button[type='submit']").ClickAsync(new());

        Assert.Equal(expected, "/" + navigation.ToBaseRelativePath(navigation.Uri));
    }
}
