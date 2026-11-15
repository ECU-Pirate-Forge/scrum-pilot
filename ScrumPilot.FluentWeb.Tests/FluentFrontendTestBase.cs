using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;
using ScrumPilot.Client;
using Microsoft.AspNetCore.Components.Rendering;
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
        Services.AddScoped<IAppNotificationService, AppNotificationService>();
        Services.AddScoped<IAppDialogService, AppDialogService>();
        Services.AddSingleton(NSubstitute.Substitute.For<IProjectWorkClient>());
        Services.AddLogging();
        Services.AddSingleton(Http.CreateClient());
        Services.AddSingleton(Http);
        RenderTree.Add<WorkTestBoundary>();

        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private sealed class WorkTestBoundary : ComponentBase, IDisposable
    {
        [Inject] public HttpClient Client { get; set; } = default!;
        [Inject] public OrganizationStateService Organizations { get; set; } = default!;
        [Inject] public ProjectStateService Projects { get; set; } = default!;
        [Inject] public MockHttpHandler Handler { get; set; } = default!;
        [Parameter] public RenderFragment? ChildContent { get; set; }
        private WorkScope? _scope;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            var organizationId = Organizations.SelectedOrganizationId ?? 1;
            _scope ??= new WorkScope(Client, organizationId, Projects.SelectedProjectId);
            Handler.DefaultOrganizationId = organizationId;
            builder.OpenComponent<CascadingValue<WorkScope>>(0);
            builder.AddAttribute(1, "Value", _scope);
            builder.AddAttribute(2, "ChildContent", ChildContent);
            builder.CloseComponent();
        }
        public void Dispose() => _scope?.Dispose();
    }

    private sealed class TestAuthService : IAuthService
    {
        public Task<bool> LoginAsync(LoginRequest request) => Task.FromResult(true);
        public Task LogoutAsync() => Task.CompletedTask;
    }

}
