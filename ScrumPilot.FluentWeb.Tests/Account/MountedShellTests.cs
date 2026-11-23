using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
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
    public void Signed_out_invitation_opens_the_login_form_and_preserves_the_return_url()
    {
        var navigation = new MountedNavigation("accept-invitation?token=diagnostic-placeholder");
        RegisterRouterSession(navigation, new SessionHandler(), authenticated: false);

        using var cut = Render<CascadingAuthenticationState>(p => p.AddChildContent<ScrumPilot.FluentWeb.App>());

        cut.WaitForAssertion(() =>
        {
            var login = cut.FindComponent<Login>();
            Assert.Equal("/fluentweb/accept-invitation?token=diagnostic-placeholder", login.Instance.ReturnUrl);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("organization-management")]
    [InlineData("organizations/3/organization-management")]
    public void Signed_out_central_route_opens_login_and_preserves_return_url(string route)
    {
        var navigation = new MountedNavigation(route);
        RegisterRouterSession(navigation, new SessionHandler(), authenticated: false);

        using var cut = Render<CascadingAuthenticationState>(
            p => p.AddChildContent<ScrumPilot.FluentWeb.App>());

        cut.WaitForAssertion(() =>
        {
            var login = Assert.Single(cut.FindComponents<Login>());
            var expected = string.IsNullOrEmpty(route)
                ? "/fluentweb/"
                : $"/fluentweb/{route}";
            Assert.Equal(expected, login.Instance.ReturnUrl);
        });
    }

    [Theory]
    [InlineData("backlog")]
    [InlineData("project-management")]
    [InlineData("scrum-board")]
    [InlineData("draft-stories")]
    [InlineData("pbigeneration")]
    [InlineData("metrics")]
    [InlineData("dependency-chart")]
    [InlineData("planning-poker")]
    [InlineData("task-planning")]
    public async Task Router_navigation_from_central_page_mounts_work_only_inside_its_boundary(string page)
    {
        var navigation = new MountedNavigation("");
        var handler = new SessionHandler(hasOrganization: true);
        RegisterRouterSession(navigation, handler);
        using var cut = Render<CascadingAuthenticationState>(p => p.AddChildContent<ScrumPilot.FluentWeb.App>());
        cut.WaitForAssertion(() => Assert.Contains("ScrumPilot dashboard", cut.Markup));

        await cut.InvokeAsync(() => navigation.NavigateTo($"organizations/3/{page}"));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Something went wrong", cut.Markup);
            var work = page is "task-planning" or "dependency-chart"
                ? Assert.Single(cut.FindComponents<PlanningComponentBase>()).Instance.Work
                : Assert.Single(cut.FindComponents<WorkComponentBase>()).Instance.Work;
            Assert.NotNull(work);
            Assert.True(work.IsCurrent);
            Assert.Equal(3, work.OrganizationId);
            Assert.Null(work.ProjectId);
        });
    }

    [Fact]
    public async Task Newly_created_organization_and_first_project_open_through_the_real_router()
    {
        var navigation = new MountedNavigation("organization-management");
        var handler = new SessionHandler();
        RegisterRouterSession(navigation, handler);
        using var cut = Render<CascadingAuthenticationState>(p => p.AddChildContent<ScrumPilot.FluentWeb.App>());
        cut.Find("fluent-text-input").Change("New organization");
        cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Create organization").Click();
        cut.WaitForAssertion(() => Assert.Contains("New organization", cut.Markup));
        var openWork = cut.FindComponents<FluentAnchorButton>().Single(button => button.Instance.ChildContent is not null
            && button.Markup.Contains("Open work", StringComparison.Ordinal));
        await cut.InvokeAsync(() => navigation.NavigateTo(openWork.Instance.Href!));
        cut.WaitForAssertion(() => Assert.Contains("Choose a project from the shell", cut.Markup));
        Assert.Equal(4, cut.FindComponent<ScrumPilot.FluentWeb.Pages.Backlog>().Instance.Work!.OrganizationId);
        await cut.InvokeAsync(() => navigation.NavigateTo("organizations/4/project-management"));
        cut.WaitForAssertion(() => Assert.Contains("No projects yet", cut.Markup));
        cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Add").Click();
        cut.Find("fluent-text-input").Change("First project");
        cut.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Save").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.EndsWith("/organizations/4/project-management?projectId=1", navigation.Uri);
            Assert.Contains("First project", cut.Markup);
            Assert.DoesNotContain("Something went wrong", cut.Markup);
            Assert.Equal(1, cut.FindComponent<ProjectManagement>().Instance.Work!.ProjectId);
        });
        await cut.InvokeAsync(() => navigation.NavigateTo("organizations/4/backlog?projectId=1"));
        cut.WaitForAssertion(() => Assert.Contains("No stories match the filters", cut.Markup));
        Assert.DoesNotContain(handler.Requests, path => path.Contains("/3/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Router_switching_organizations_with_equal_project_ids_retires_the_old_scope()
    {
        var navigation = new MountedNavigation("organizations/3/backlog?projectId=1");
        var handler = new SessionHandler(hasProject: true);
        RegisterRouterSession(navigation, handler);
        using var cut = Render<CascadingAuthenticationState>(p => p.AddChildContent<ScrumPilot.FluentWeb.App>());
        var previous = cut.FindComponent<ScrumPilot.FluentWeb.Pages.Backlog>().Instance.Work!;

        await cut.InvokeAsync(() => navigation.NavigateTo("organizations/2/backlog?projectId=1"));

        cut.WaitForAssertion(() =>
        {
            var current = cut.FindComponent<ScrumPilot.FluentWeb.Pages.Backlog>().Instance.Work!;
            Assert.Equal(2, current.OrganizationId);
            Assert.Equal(1, current.ProjectId);
            Assert.True(current.IsCurrent);
            Assert.False(previous.IsCurrent);
            Assert.DoesNotContain("Something went wrong", cut.Markup);
        });
    }

    private void RegisterRouterSession(MountedNavigation navigation, SessionHandler handler, bool authenticated = true)
    {
        Services.AddFluentUIComponents();
        var authorization = this.AddAuthorization();
        if (authenticated)
        {
            authorization.SetAuthorized("Test");
            authorization.SetRoles("Admin");
            authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, "test-owner"));
        }
        Services.AddSingleton<NavigationManager>(navigation);
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.test/fluentweb/_api/webapi/") });
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        Services.AddScoped<AppThemeService>();
        Services.AddScoped<IAppNotificationService, AppNotificationService>();
        Services.AddScoped<IAppDialogService, AppDialogService>();
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(Substitute.For<IProjectWorkClient>());
        Services.AddLogging();
        Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task Landing_page_stays_on_home_and_hydrates_default_workspace()
    {
        var navigation = new MountedNavigation("", deferLocationChanged: true);
        var handler = new SessionHandler(hasProject: true, useDefaultProject: true);
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
            if (OrganizationRoutes.OrganizationId(new Uri(navigation.Uri)) is null)
                builder.AddContent(0, "Central landing page");
            else
            {
                builder.OpenComponent<ScrumPilot.FluentWeb.Pages.Backlog>(0);
                builder.CloseComponent();
            }
        }));

        Assert.EndsWith("/fluentweb/", navigation.Uri);
        Assert.Contains("Central landing page", cut.Markup);
        Assert.DoesNotContain("Something went wrong", cut.Markup);
        await cut.InvokeAsync(navigation.DispatchLocationChanged);
        cut.WaitForAssertion(() =>
        {
            Assert.EndsWith("/fluentweb/", navigation.Uri);
            Assert.Contains("Central landing page", cut.Markup);
            Assert.DoesNotContain("Something went wrong", cut.Markup);
            Assert.Equal(3, Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
            Assert.Equal(1, Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        });
    }

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
        Assert.Equal("", cut.FindComponents<FluentNavItem>().First().Instance.Href);
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
        private readonly bool _deferLocationChanged;
        public MountedNavigation(string route, bool deferLocationChanged = false)
        {
            _deferLocationChanged = deferLocationChanged;
            Initialize("https://gateway.test/fluentweb/", "https://gateway.test/fluentweb/" + route);
        }
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            if (!_deferLocationChanged) NotifyLocationChanged(false);
        }
        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            NavigateToCore(uri, options.ForceLoad);
        public void DispatchLocationChanged() => NotifyLocationChanged(false);
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

    private sealed class SessionHandler(bool hasProject = false, bool useDefaultProject = false, bool hasOrganization = false) : HttpMessageHandler
    {
        private bool _createdOrganization;
        private bool _createdProject;
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            if (request.Method == HttpMethod.Post && path.EndsWith("/organizations", StringComparison.Ordinal))
            {
                _createdOrganization = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new OrganizationSummaryDto(4, "New organization", OrganizationRole.Owner, false))
                });
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/4/projects", StringComparison.Ordinal))
            {
                _createdProject = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new Project { OrganizationId = 4, ProjectId = 1, ProjectName = "First project" })
                });
            }
            if (path.EndsWith("/guidelines", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            object payload = path.EndsWith("/settings", StringComparison.Ordinal)
            ? new UserSettingsDto
            {
                DefaultOrganizationId = useDefaultProject ? 3 : null,
                DefaultProjectId = useDefaultProject ? 1 : null
            }
            : _createdOrganization && path.EndsWith("/organizations", StringComparison.Ordinal)
                ? new[] { new OrganizationSummaryDto(4, "New organization", OrganizationRole.Owner, false) }
                : _createdProject && path.EndsWith("/4/projects", StringComparison.Ordinal)
                    ? new[] { new Project { OrganizationId = 4, ProjectId = 1, ProjectName = "First project" } }
                : path.EndsWith("/operations", StringComparison.Ordinal)
                    ? new { OrganizationId = 4, State = "Active", OperationId = (string?)null, Error = (string?)null }
            : (hasProject || hasOrganization) && path.EndsWith("/organizations", StringComparison.Ordinal)
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
