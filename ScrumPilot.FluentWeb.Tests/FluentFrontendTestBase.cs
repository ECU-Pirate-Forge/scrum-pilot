using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace ScrumPilot.FluentWeb.Tests;

public abstract class FluentFrontendTestBase : BunitContext
{
    protected FluentFrontendTestBase()
    {
        Services.AddFluentUIComponents();
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider, TestAuthenticationStateProvider>();
        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "Test User"),
                    new Claim(ClaimTypes.NameIdentifier, "test-user")
                },
                "TestAuth");

            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
