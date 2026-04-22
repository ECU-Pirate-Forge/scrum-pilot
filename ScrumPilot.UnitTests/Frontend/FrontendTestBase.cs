using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using ScrumPilot.Web.Services;

namespace ScrumPilot.UnitTests.Frontend
{
    public abstract class FrontendTestBase : BunitContext
    {
        protected readonly HttpClient MockHttpClient;

        protected FrontendTestBase()
        {
            Services.AddMudServices();
            MockHttpClient = Substitute.For<HttpClient>();
            Services.AddSingleton(MockHttpClient);

            // Register auth so components that inject AuthenticationStateProvider
            // (Home, PbiCard, CommentThread, etc.) don't throw MissingBunitAuthorizationException.
            // Uses bUnit's own test-double extension, not the ASP.NET Core one.
            this.AddAuthorization();

            // Register ProjectStateService so pages that inject it
            // (ScrumBoard, SwimLanes, Backlog, PbiGeneration, etc.) can be rendered.
            Services.AddSingleton<ProjectStateService>();

            // Register MetricsDashboardService so Backlog (and other pages that inject it) can be rendered.
            Services.AddSingleton(new MetricsDashboardService(MockHttpClient));

            // Use loose mode to ignore JSInterop issues - focus on component logic instead
            JSInterop.Mode = JSRuntimeMode.Loose;

            // Render MudPopoverProvider so components that use MudTooltip, MudChip, etc.
            // can register with the shared IPopoverService during initialisation.
            Render<MudPopoverProvider>();
        }
    }
}

