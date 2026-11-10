using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;
using ScrumPilot.FluentWeb.Tests.TestHelpers;

namespace ScrumPilot.FluentWeb.Tests;

public abstract class FluentFrontendTestBase : BunitContext
{
    protected BunitAuthorizationContext Authorization { get; }

    /// <summary>Route-based HTTP fake backing the injected HttpClient. Register routes before rendering.</summary>
    protected MockHttpHandler Http { get; } = new();

    protected FluentFrontendTestBase()
    {
        Services.AddFluentUIComponents();
        Authorization = AddAuthorization();
        Authorization.SetAuthorized("Test User");
        Services.AddScoped<IAuthService, TestAuthService>();
        Services.AddSingleton<ProjectStateService>();
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<AppThemeService>();
        Services.AddScoped<MetricsDashboardService>();
        Services.AddScoped<IAppNotificationService, AppNotificationService>();
        Services.AddScoped<IAppDialogService, AppDialogService>();
        Services.AddLogging();
        Services.AddSingleton(Http.CreateClient());

        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private sealed class TestAuthService : IAuthService
    {
        public Task<bool> LoginAsync(LoginRequest request) => Task.FromResult(true);
        public Task LogoutAsync() => Task.CompletedTask;
    }

}
