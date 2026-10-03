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
    [InlineData("/accept-invitation?token=invitation-token", "/accept-invitation?token=invitation-token")]
    [InlineData("//evil.example/path", "/")]
    [InlineData("/\\evil.example/path", "/")]
    [InlineData("\\\\evil.example/path", "/")]
    [InlineData("https://evil.example/path", "/")]
    [InlineData(" \t/accept-invitation", "/")]
    [InlineData("/accept-invitation\r\n", "/")]
    [InlineData("/%5cevil.example", "/")]
    [InlineData("/%255cevil.example", "/")]
    [InlineData("/%252fevil.example", "/")]
    [InlineData("/%252F%252Fevil.example", "/")]
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
