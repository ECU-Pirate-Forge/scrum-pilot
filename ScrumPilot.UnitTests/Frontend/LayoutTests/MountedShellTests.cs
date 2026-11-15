using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
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
        public MountedNavigation(string route) => Initialize("https://gateway.test/web/", "https://gateway.test/web/" + route);
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
                ? new[] { new OrganizationSummaryDto(3, "BETA", OrganizationRole.Owner, false) }
                : hasProject && path.EndsWith("/projects", StringComparison.Ordinal)
                    ? new[] { new Project { OrganizationId = 3, ProjectId = 1, ProjectName = "BETA project" } }
                    : Array.Empty<object>();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            });
        }
    }
}
