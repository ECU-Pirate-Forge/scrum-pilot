using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.FluentWeb.Tests;

public abstract class FluentFrontendTestBase : BunitContext
{
    protected FluentFrontendTestBase()
    {
        Services.AddFluentUIComponents();
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider, TestAuthenticationStateProvider>();
        Services.AddScoped<IAuthService, TestAuthService>();
        Services.AddSingleton<ProjectStateService>();
        Services.AddSingleton(new HttpClient(new StubHttpMessageHandler()) { BaseAddress = new Uri("https://localhost/") });

        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "Test User"),
                    new Claim(ClaimTypes.NameIdentifier, "test-user")
                ],
                "TestAuth");

            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private sealed class TestAuthService : IAuthService
    {
        public Task<bool> LoginAsync(LoginRequest request) => Task.FromResult(true);
        public Task LogoutAsync() => Task.CompletedTask;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
