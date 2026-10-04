using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using ScrumPilot.FluentWeb.Auth;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.FluentWeb.Tests;

public abstract class FluentFrontendTestBase : BunitContext
{
    protected FluentFrontendTestBase()
    {
        Services.AddFluentUIComponents();
        Services.AddAuthorizationCore();

        Services.AddScoped<JwtAuthStateProvider>();
        Services.AddScoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<JwtAuthStateProvider>());
        Services.AddScoped<IAuthService, TestAuthService>();
        Services.AddSingleton<ProjectStateService>();

        var jsRuntime = new BunitJSInterop();
        jsRuntime.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IJSRuntime>(jsRuntime);

        Services.AddSingleton(new HttpClient(new RecordingHttpMessageHandler()));
    }

    private sealed class TestAuthService : IAuthService
    {
        public Task<bool> LoginAsync(LoginRequest request) => Task.FromResult(true);
        public Task LogoutAsync() => Task.CompletedTask;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
