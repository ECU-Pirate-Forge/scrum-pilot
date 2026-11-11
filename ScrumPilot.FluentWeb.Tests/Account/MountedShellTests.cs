using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;
using ScrumPilot.Client;
using ScrumPilot.Shared.Models;
using ScrumPilot.FluentWeb.Layout;
using ScrumPilot.FluentWeb.Pages;
using ScrumPilot.FluentWeb.Services;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Account;

public sealed class MountedShellTests : BunitContext
{
    [Fact]
    public async Task Navigation_links_follow_the_mounted_url_before_session_load_and_after_tenant_switch()
    {
        var navigation = new MountedNavigation("organizations/3/backlog");
        Services.AddFluentUIComponents();
        Services.AddSingleton<NavigationManager>(navigation);
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        using var cut = Render<NavMenu>();
        foreach (var page in new[] { "backlog", "metrics", "planning-poker", "dependency-chart", "draft-stories", "project-management", "scrum-board", "pbigeneration" })
            Assert.Contains($"organizations/3/{page}", cut.FindComponents<FluentNavItem>().Select(item => item.Instance.Href));

        await cut.InvokeAsync(() => navigation.NavigateTo("organizations/2/backlog?projectId=1"));

        foreach (var page in new[] { "backlog", "metrics", "planning-poker" })
            Assert.Contains($"organizations/2/{page}?projectId=1", cut.FindComponents<FluentNavItem>().Select(item => item.Instance.Href));
    }

    [Fact]
    public async Task First_project_selection_from_real_backlog_boundary_uses_the_radio_change_event()
    {
        var navigation = new MountedNavigation("organizations/3/backlog");
        var handler = new SessionHandler(hasProject: true);
        Services.AddFluentUIComponents();
        Services.AddSingleton<NavigationManager>(navigation);
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.test/fluentweb/_api/webapi/") });
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        Services.AddScoped<AppThemeService>();
        Services.AddScoped<IAppNotificationService, AppNotificationService>();
        Services.AddScoped<IAppDialogService, AppDialogService>();
        Services.AddSingleton<AuthenticationStateProvider>(new AdminAuthentication());
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddLogging();
        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
        JSInterop.Mode = JSRuntimeMode.Loose;
        using var cut = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<ScrumPilot.FluentWeb.Pages.Backlog>(0);
            builder.CloseComponent();
            builder.OpenComponent<BoundaryProbe>(1);
            builder.CloseComponent();
        }));
        cut.WaitForAssertion(() => Assert.Contains("Choose a project from the shell", cut.Markup));
        var oldScope = cut.FindComponent<BoundaryProbe>().Instance.Work;
        Assert.True(oldScope.IsCurrent);
        Assert.Null(oldScope.ProjectId);

        var item = cut.FindAll("fluent-menu-item").Single(element => element.TextContent.Trim() == "BETA project");
        await item.TriggerEventAsync("onmenuitemchange", new MenuItemEventArgs { Id = item.Id, Checked = true });

        cut.WaitForAssertion(() => Assert.EndsWith("/fluentweb/organizations/3/backlog?projectId=1", navigation.Uri));
        Assert.False(oldScope.IsCurrent);
        Assert.Equal(1, cut.FindComponent<BoundaryProbe>().Instance.Work.ProjectId);
        Assert.Equal("Select project: BETA project", cut.Find("[title='Select project']").GetAttribute("aria-label"));
        Assert.Equal("Select organization: BETA", cut.Find("[title='Select organization']").GetAttribute("aria-label"));
        Assert.Contains(handler.Requests, path => path.Contains("/3/pbi/getNonDraftPbis", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Choose a project from the shell", cut.Markup);

        var beta = cut.FindAll("fluent-menu-item").Single(element => element.TextContent.Trim() == "BETA");
        await beta.TriggerEventAsync("onmenuitemchange", new MenuItemEventArgs { Id = beta.Id, Checked = false });
        Assert.EndsWith("/organizations/3/backlog?projectId=1", navigation.Uri);
        var alpha = cut.FindAll("fluent-menu-item").Single(element => element.TextContent.Trim() == "ALPHA");
        await alpha.TriggerEventAsync("onmenuitemchange", new MenuItemEventArgs { Id = alpha.Id, Checked = true });
        cut.WaitForAssertion(() => Assert.EndsWith("/organizations/2/backlog", navigation.Uri));
        Assert.Null(cut.FindComponent<BoundaryProbe>().Instance.Work.ProjectId);
        Assert.Contains("Choose a project from the shell", cut.Markup);
    }

    [Theory]
    [InlineData("")]
    [InlineData("organization-management")]
    public void Mounted_central_pages_remain_reachable_without_an_organization(string route)
    {
        var navigation = new MountedNavigation(route);
        var handler = new SessionHandler();
        Services.AddFluentUIComponents();
        Services.AddSingleton<NavigationManager>(navigation);
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.test/fluentweb/_api/webapi/") });
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        Services.AddScoped<AppThemeService>();
        Services.AddScoped<IAppNotificationService, AppNotificationService>();
        Services.AddScoped<IAppDialogService, AppDialogService>();
        Services.AddSingleton<AuthenticationStateProvider>(new AdminAuthentication());
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddLogging();
        Renderer.SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("WebAssembly", true));
        JSInterop.Mode = JSRuntimeMode.Loose;

        using var cut = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            if (route.Length == 0) builder.AddContent(0, "Central landing page");
            else
            {
                builder.OpenComponent<OrganizationManagement>(0);
                builder.CloseComponent();
            }
        }));

        Assert.Equal("https://gateway.test/fluentweb/", navigation.BaseUri);
        Assert.Equal("https://gateway.test/fluentweb/" + route, navigation.Uri);
        cut.WaitForAssertion(() => Assert.Contains(route.Length == 0 ? "Central landing page" : "Create organization", cut.Markup));
        Assert.DoesNotContain("Organization unavailable", cut.Markup);
        Assert.DoesNotContain(handler.Requests, path => path.Contains("/projects", StringComparison.Ordinal));
        Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
    }

    private sealed class MountedNavigation : NavigationManager
    {
        public MountedNavigation(string route) => Initialize("https://gateway.test/fluentweb/", "https://gateway.test/fluentweb/" + route);
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }

    private sealed class AdminAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Test"), new Claim(ClaimTypes.Role, "Admin")], "test"))));
    }

    public sealed class BoundaryProbe : ComponentBase
    {
        [CascadingParameter] public WorkScope Work { get; set; } = default!;
    }

    private sealed class SessionHandler(bool hasProject = false) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            object payload = path.EndsWith("/settings", StringComparison.Ordinal)
            ? new UserSettingsDto()
            : hasProject && path.EndsWith("/organizations", StringComparison.Ordinal)
                ? new[] { new OrganizationSummaryDto(2, "ALPHA", OrganizationRole.Owner, false), new OrganizationSummaryDto(3, "BETA", OrganizationRole.Owner, false) }
                : hasProject && path.EndsWith("/projects", StringComparison.Ordinal)
                    ? new[] { new Project { OrganizationId = path.Contains("/2/") ? 2 : 3, ProjectId = 1, ProjectName = path.Contains("/2/") ? "ALPHA project" : "BETA project" } }
                    : Array.Empty<object>();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            });
        }
    }
}
