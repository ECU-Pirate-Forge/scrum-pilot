using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using MudBlazor;
using NSubstitute;
using ScrumPilot.Client;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Layout;
using ScrumPilot.Web.Pages;
using ScrumPilot.Web.Services;

namespace ScrumPilot.UnitTests.Frontend.LayoutTests;

public sealed class MountedShellTests : BunitContext
{
    [Fact]
    public async Task DefaultProjectRedirectWaitsForLocationChangedBeforeMountingWork()
    {
        var navigation = new MountedNavigation("", deferLocationChanged: true);
        var handler = new SessionHandler(hasProject: true, useDefaultProject: true);
        Services.AddMudServices();
        Services.AddSingleton<NavigationManager>(navigation);
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.test/web/_api/webapi/") });
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        Services.AddSingleton<AuthenticationStateProvider>(new AdminAuthentication());
        Services.AddSingleton(Substitute.For<IAuthService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        using var cut = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            if (OrganizationRoutes.OrganizationId(new Uri(navigation.Uri)) is null)
                builder.AddContent(0, "Central landing page");
            else
            {
                builder.OpenComponent<ScrumPilot.Web.Pages.Backlog>(0);
                builder.CloseComponent();
            }
        }));

        Assert.EndsWith("/organizations/3/backlog?projectId=1", navigation.Uri);
        Assert.Empty(cut.FindComponents<ScrumPilot.Web.Pages.Backlog>());
        await cut.InvokeAsync(navigation.DispatchLocationChanged);
        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<ScrumPilot.Web.Pages.Backlog>()));
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
    public async Task RouterNavigationFromCentralPageMountsWorkOnlyInsideItsBoundary(string page)
    {
        var navigation = new MountedNavigation("");
        var handler = new SessionHandler(hasOrganization: true);
        RegisterRouterSession(navigation, handler);
        using var cut = Render<ScrumPilot.Web.App>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<MainLayout>()));

        await cut.InvokeAsync(() => navigation.NavigateTo($"organizations/3/{page}"));

        cut.WaitForAssertion(() =>
        {
            var work = Assert.Single(cut.FindComponents<WorkComponentBase>()).Instance.Work;
            Assert.NotNull(work);
            Assert.True(work.IsCurrent);
            Assert.Equal(3, work.OrganizationId);
            Assert.Null(work.ProjectId);
        });
    }

    [Fact]
    public async Task NewlyCreatedOrganizationAndFirstProjectOpenThroughTheRealRouter()
    {
        var navigation = new MountedNavigation("organization-management");
        var handler = new SessionHandler();
        RegisterRouterSession(navigation, handler);
        using var cut = Render<ScrumPilot.Web.App>();
        var name = cut.FindComponents<MudTextField<string>>().Single(field => field.Instance.Label == "Organization name");
        await cut.InvokeAsync(() => name.Instance.ValueChanged.InvokeAsync("New organization"));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Create").Click();
        cut.WaitForAssertion(() => Assert.Contains("New organization", cut.Markup));
        var openWork = cut.FindComponents<MudButton>().Single(button => button.Markup.Contains("Open work", StringComparison.Ordinal));
        await cut.InvokeAsync(() => navigation.NavigateTo(openWork.Instance.Href!));
        cut.WaitForAssertion(() => Assert.Single(cut.FindComponents<ScrumPilot.Web.Pages.Backlog>()));
        Assert.Equal(4, cut.FindComponent<ScrumPilot.Web.Pages.Backlog>().Instance.Work!.OrganizationId);
        await cut.InvokeAsync(() => navigation.NavigateTo("organizations/4/project-management"));
        cut.WaitForAssertion(() => Assert.Contains("No projects yet", cut.Markup));
        cut.Find("button[title='Add Project']").Click();
        var projectName = cut.FindComponents<MudTextField<string>>().Single(field => field.Instance.Label == "Project Name");
        await cut.InvokeAsync(() => projectName.Instance.ValueChanged.InvokeAsync("First project"));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Save").Click();
        cut.WaitForAssertion(() => Assert.Contains("First project", cut.Markup));
        cut.Find("button[aria-label='Select project: none']").Click();
        var item = cut.FindComponents<MudMenuItem>().Single(menu => menu.Markup.Contains("First project", StringComparison.Ordinal));
        await cut.InvokeAsync(() => item.Instance.OnClick.InvokeAsync(new()));
        cut.WaitForAssertion(() =>
        {
            Assert.EndsWith("/organizations/4/project-management?projectId=1", navigation.Uri);
            Assert.Equal(1, cut.FindComponent<ProjectManagement>().Instance.Work!.ProjectId);
        });
        await cut.InvokeAsync(() => navigation.NavigateTo("organizations/4/backlog?projectId=1"));
        cut.WaitForAssertion(() => Assert.Equal(1, cut.FindComponent<ScrumPilot.Web.Pages.Backlog>().Instance.Work!.ProjectId));
        Assert.DoesNotContain(handler.Requests, path => path.Contains("/3/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RouterSwitchingOrganizationsWithEqualProjectIdsRetiresTheOldScope()
    {
        var navigation = new MountedNavigation("organizations/3/backlog?projectId=1");
        var handler = new SessionHandler(hasProject: true);
        RegisterRouterSession(navigation, handler);
        using var cut = Render<ScrumPilot.Web.App>();
        var previous = cut.FindComponent<ScrumPilot.Web.Pages.Backlog>().Instance.Work!;

        await cut.InvokeAsync(() => navigation.NavigateTo("organizations/2/backlog?projectId=1"));

        cut.WaitForAssertion(() =>
        {
            var current = cut.FindComponent<ScrumPilot.Web.Pages.Backlog>().Instance.Work!;
            Assert.Equal(2, current.OrganizationId);
            Assert.Equal(1, current.ProjectId);
            Assert.True(current.IsCurrent);
            Assert.False(previous.IsCurrent);
        });
    }

    private void RegisterRouterSession(MountedNavigation navigation, SessionHandler handler)
    {
        Services.AddMudServices();
        var authorization = this.AddAuthorization();
        authorization.SetAuthorized("Test");
        authorization.SetRoles("Admin");
        authorization.SetClaims(new Claim(ClaimTypes.NameIdentifier, "test-owner"));
        Services.AddSingleton<NavigationManager>(navigation);
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.test/web/_api/webapi/") });
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        Services.AddSingleton(Substitute.For<IAuthService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task FirstProjectSelectionFromRealBacklogBoundaryNavigatesAndRemountsScope()
    {
        var navigation = new MountedNavigation("organizations/3/backlog");
        var handler = new SessionHandler(hasProject: true);
        Services.AddMudServices();
        Services.AddSingleton<NavigationManager>(navigation);
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.test/web/_api/webapi/") });
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        Services.AddSingleton<AuthenticationStateProvider>(new AdminAuthentication());
        Services.AddSingleton(Substitute.For<IAuthService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        using var cut = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<ScrumPilot.Web.Pages.Backlog>(0);
            builder.CloseComponent();
            builder.OpenComponent<BoundaryProbe>(1);
            builder.CloseComponent();
        }));
        var oldScope = cut.FindComponent<BoundaryProbe>().Instance.Work;
        Assert.True(oldScope.IsCurrent);
        Assert.Null(oldScope.ProjectId);
        Assert.NotNull(cut.Find("button[aria-label='Select organization: BETA']"));
        Assert.NotNull(cut.Find("button[aria-label='Select project: none']"));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Select Project").Click();
        var item = cut.FindComponents<MudMenuItem>().Single(component => component.Markup.Contains("BETA project", StringComparison.Ordinal));
        await cut.InvokeAsync(() => item.Instance.OnClick.InvokeAsync(new()));

        cut.WaitForAssertion(() => Assert.EndsWith("/web/organizations/3/backlog?projectId=1", navigation.Uri));
        Assert.False(oldScope.IsCurrent);
        Assert.Equal(1, cut.FindComponent<BoundaryProbe>().Instance.Work.ProjectId);
        Assert.NotNull(cut.Find("button[aria-label='Select project: BETA project']"));
        Assert.Contains(handler.Requests, path => path.Contains("/3/pbi/getNonDraftPbis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NavigationLinksFollowMountedUrlAfterTenantAndProjectSwitch()
    {
        var navigation = new MountedNavigation("organizations/3/backlog");
        Services.AddMudServices();
        Services.AddSingleton<NavigationManager>(navigation);
        JSInterop.Mode = JSRuntimeMode.Loose;
        using var cut = Render<NavMenu>();
        Assert.Contains("organizations/3/metrics", cut.FindComponents<MudNavLink>().Select(item => item.Instance.Href));

        await cut.InvokeAsync(() => navigation.NavigateTo("organizations/2/backlog?projectId=1"));

        foreach (var page in new[] { "backlog", "metrics", "planning-poker" })
            Assert.Contains($"organizations/2/{page}?projectId=1", cut.FindComponents<MudNavLink>().Select(item => item.Instance.Href));
    }

    [Theory]
    [InlineData("")]
    [InlineData("organization-management")]
    public void MountedCentralPagesRemainReachableWithoutAnOrganization(string route)
    {
        var navigation = new MountedNavigation(route);
        var handler = new SessionHandler();
        Services.AddMudServices();
        Services.AddSingleton<NavigationManager>(navigation);
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://gateway.test/web/_api/webapi/") });
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        Services.AddSingleton<AuthenticationStateProvider>(new AdminAuthentication());
        Services.AddSingleton(Substitute.For<IAuthService>());
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

        Assert.Equal("https://gateway.test/web/", navigation.BaseUri);
        Assert.Equal("https://gateway.test/web/" + route, navigation.Uri);
        cut.WaitForAssertion(() => Assert.Contains(route.Length == 0 ? "Central landing page" : "Create organization", cut.Markup));
        Assert.DoesNotContain("Unable to open this organization", cut.Markup);
        Assert.DoesNotContain(handler.Requests, path => path.Contains("/projects", StringComparison.Ordinal));
        Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
    }

    private sealed class MountedNavigation : NavigationManager
    {
        private readonly bool _deferLocationChanged;
        public MountedNavigation(string route, bool deferLocationChanged = false)
        {
            _deferLocationChanged = deferLocationChanged;
            Initialize("https://gateway.test/web/", "https://gateway.test/web/" + route);
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
