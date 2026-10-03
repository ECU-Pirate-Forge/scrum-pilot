using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Layout;
using ScrumPilot.Web.Services;

namespace ScrumPilot.UnitTests.Frontend.LayoutTests;

public class MainLayoutTests : BunitContext
{
    private readonly TestAuthenticationStateProvider _authentication = new(true);
    private readonly RecordingHttpHandler _handler = new();

    public MainLayoutTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(new HttpClient(_handler) { BaseAddress = new Uri("https://localhost/") });
        Services.AddScoped<OrganizationStateService>();
        Services.AddScoped<ProjectStateService>();
        Services.AddSingleton<AuthenticationStateProvider>(_authentication);
        Services.AddSingleton(Substitute.For<IAuthService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void AuthenticatedStartup_SelectsAccessibleDefaultsAndUsesOrganizationRoute()
    {
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Default")
        });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            Email = "user@example.com",
            DiscordUsername = "user",
            UiPreference = UiPreference.Dark,
            DefaultOrganizationId = 2,
            DefaultProjectId = 22
        });
        _handler.Respond("api/organizations/2/projects", new[]
        {
            Project(21, 2, "Other"),
            Project(22, 2, "Default Project")
        });

        Render<MainLayout>();

        Assert.Equal(2, Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
        Assert.Equal(22, Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        Assert.Contains("api/organizations/2/projects", _handler.Requests);
        Assert.DoesNotContain("api/project", _handler.Requests, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Startup_IgnoresInaccessibleDefaultsAndSelectsFirstAccessibleItems()
    {
        _handler.Respond("api/organizations", new[] { Organization(4, "Accessible") });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            DefaultOrganizationId = 999,
            DefaultProjectId = 888
        });
        _handler.Respond("api/organizations/4/projects", new[]
        {
            Project(41, 4, "Accessible Project")
        });

        var cut = Render<MainLayout>();

        Assert.Equal(4, Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
        Assert.Equal(41, Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        Assert.Contains("Accessible", cut.Markup);
        Assert.DoesNotContain("Inaccessible", cut.Markup);
    }

    [Fact]
    public async Task OrganizationChange_ClearsProjectLoadsScopedRouteAndPersistsMergedSettings()
    {
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Second")
        });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            Email = "keep@example.com",
            DiscordUsername = "keep-discord",
            UiPreference = UiPreference.Dark,
            DefaultOrganizationId = 1,
            DefaultProjectId = 11
        });
        _handler.Respond("api/organizations/1/projects", new[] { Project(11, 1, "Old") });
        var projectsResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.RespondAsync("api/organizations/2/projects", _ => projectsResponse.Task);
        _handler.RespondStatus("api/user/settings", HttpMethod.Put, HttpStatusCode.NoContent);

        var cut = Render<MainLayout>();
        cut.FindAll("button").Single(button => button.TextContent.Contains("First")).Click();
        var second = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Second"));

        Task selectionTask = Task.CompletedTask;
        await cut.InvokeAsync(() =>
        {
            selectionTask = second.Instance.OnClick.InvokeAsync(new MouseEventArgs());
        });

        Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProject);
        Assert.Contains("api/organizations/2/projects", _handler.Requests);

        projectsResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { Project(21, 2, "New") })
        });
        await selectionTask;

        var saved = Assert.Single(_handler.JsonBodies);
        Assert.Equal("keep@example.com", saved.Email);
        Assert.Equal("keep-discord", saved.DiscordUsername);
        Assert.Equal(UiPreference.Dark, saved.UiPreference);
        Assert.Equal(2, saved.DefaultOrganizationId);
        Assert.Equal(21, saved.DefaultProjectId);
    }

    [Fact]
    public async Task AuthenticationLoss_ClearsOrganizationAndProjectState()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "First") });
        _handler.Respond("api/user/settings", new UserSettingsDto());
        _handler.Respond("api/organizations/1/projects", new[] { Project(11, 1, "Project") });
        var cut = Render<MainLayout>();

        _authentication.SetAuthenticated(false);

        cut.WaitForAssertion(() =>
        {
            Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganization);
            Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProject);
        });
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LateProjectResponse_DoesNotOverrideNewOrganizationSelection()
    {
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Second")
        });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            DefaultOrganizationId = 1
        });
        var staleResponse = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.RespondAsync("api/organizations/1/projects", _ => staleResponse.Task);
        _handler.Respond("api/organizations/2/projects", new[] { Project(21, 2, "Current") });
        _handler.RespondStatus("api/user/settings", HttpMethod.Put, HttpStatusCode.NoContent);

        var cut = Render<MainLayout>();
        cut.FindAll("button").Single(button => button.TextContent.Contains("First")).Click();
        var second = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Second"));
        await cut.InvokeAsync(() => second.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        staleResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { Project(11, 1, "Stale") })
        });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
            Assert.Equal(21, Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        });
    }

    [Fact]
    public async Task ProjectChange_PersistsSelectionWithoutOverwritingOtherSettings()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Organization") });
        var settingsRequest = 0;
        _handler.RespondAsync("api/user/settings", _ =>
        {
            settingsRequest++;
            var settings = settingsRequest == 1
                ? new UserSettingsDto
                {
                    Email = "old@example.com",
                    DiscordUsername = "old-discord",
                    UiPreference = UiPreference.Light,
                    DefaultOrganizationId = 1,
                    DefaultProjectId = 11
                }
                : new UserSettingsDto
                {
                    Email = "latest@example.com",
                    DiscordUsername = "latest-discord",
                    UiPreference = UiPreference.Dark,
                    DefaultOrganizationId = 1,
                    DefaultProjectId = 11
                };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(settings)
            });
        });
        _handler.Respond("api/organizations/1/projects", new[]
        {
            Project(11, 1, "First Project"),
            Project(12, 1, "Second Project")
        });
        _handler.RespondStatus("api/user/settings", HttpMethod.Put, HttpStatusCode.NoContent);
        var cut = Render<MainLayout>();
        cut.FindAll("button").Single(button => button.TextContent.Contains("First Project")).Click();
        var second = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Second Project"));

        await cut.InvokeAsync(() => second.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        var saved = Assert.Single(_handler.JsonBodies);
        Assert.Equal("latest@example.com", saved.Email);
        Assert.Equal("latest-discord", saved.DiscordUsername);
        Assert.Equal(UiPreference.Dark, saved.UiPreference);
        Assert.Equal(1, saved.DefaultOrganizationId);
        Assert.Equal(12, saved.DefaultProjectId);
    }

    [Fact]
    public void ConfirmedLogout_ClearsBothSelections()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Organization") });
        _handler.Respond("api/user/settings", new UserSettingsDto());
        _handler.Respond("api/organizations/1/projects", new[] { Project(11, 1, "Project") });
        var cut = Render<MainLayout>();

        cut.Find("[title='Sign out']").Click();
        cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Sign Out")
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganization);
            Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProject);
            Services.GetRequiredService<IAuthService>().Received(1).LogoutAsync();
        });
    }

    [Fact]
    public void OrganizationLoadFailure_ShowsSnackbarAndKeepsEmptyState()
    {
        _handler.RespondStatus("api/organizations", HttpMethod.Get, HttpStatusCode.InternalServerError);

        var cut = Render<MainLayout>();

        cut.WaitForAssertion(() =>
        {
            Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganization);
            Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProject);
            Assert.Contains("Unable to load organizations and projects.", cut.Markup);
        });
    }

    [Fact]
    public async Task Startup_WhenEveryOrganizationIsStale_AttemptsEachOnceAndStops()
    {
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Second")
        });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            DefaultOrganizationId = 1
        });
        _handler.RespondStatus(
            "api/organizations/1/projects", HttpMethod.Get, HttpStatusCode.NotFound);
        _handler.RespondStatus(
            "api/organizations/2/projects", HttpMethod.Get, HttpStatusCode.NotFound);

        var cut = Render<MainLayout>();

        cut.WaitForAssertion(() =>
        {
            Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganization);
            Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProject);
            Assert.Contains("Unable to load organizations and projects.", cut.Markup);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(
                cut.Markup, "Unable to load organizations and projects\\.").Count);
            Assert.Equal(1, _handler.Requests.Count(r => r == "api/organizations/1/projects"));
            Assert.Equal(1, _handler.Requests.Count(r => r == "api/organizations/2/projects"));
        });
        var requestCount = _handler.Requests.Count;

        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(requestCount, _handler.Requests.Count);
    }

    [Fact]
    public async Task RapidProjectChanges_SerializeWritesAndPersistLatestSelectionLast()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Organization") });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            Email = "keep@example.com",
            DefaultOrganizationId = 1,
            DefaultProjectId = 11
        });
        _handler.Respond("api/organizations/1/projects", new[]
        {
            Project(11, 1, "First"),
            Project(12, 1, "Second"),
            Project(13, 1, "Third")
        });
        var firstPut = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var activePuts = 0;
        var maximumActivePuts = 0;
        var putCount = 0;
        _handler.RespondAsync("api/user/settings", HttpMethod.Put, async _ =>
        {
            var active = Interlocked.Increment(ref activePuts);
            maximumActivePuts = Math.Max(maximumActivePuts, active);
            var currentPut = Interlocked.Increment(ref putCount);
            try
            {
                return currentPut == 1
                    ? await firstPut.Task
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            finally
            {
                Interlocked.Decrement(ref activePuts);
            }
        });
        var cut = Render<MainLayout>();
        cut.FindAll("button").Single(button => button.TextContent.Contains("First")).Click();
        var second = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Second"));
        var third = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Third"));

        Task secondSelection = Task.CompletedTask;
        await cut.InvokeAsync(() =>
        {
            secondSelection = second.Instance.OnClick.InvokeAsync(new MouseEventArgs());
        });
        cut.WaitForAssertion(() => Assert.Single(_handler.JsonBodies));

        Task thirdSelection = Task.CompletedTask;
        var thirdDispatch = cut.InvokeAsync(() =>
        {
            thirdSelection = third.Instance.OnClick.InvokeAsync(new MouseEventArgs());
        });
        cut.WaitForAssertion(() =>
            Assert.Equal(13, Services.GetRequiredService<ProjectStateService>().SelectedProjectId));
        Assert.Single(_handler.JsonBodies);

        firstPut.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await Task.WhenAll(secondSelection, thirdDispatch);
        await thirdSelection;

        Assert.Equal(2, _handler.JsonBodies.Count);
        Assert.Equal(12, _handler.JsonBodies[0].DefaultProjectId);
        Assert.Equal(13, _handler.JsonBodies[1].DefaultProjectId);
        Assert.Equal(1, maximumActivePuts);
    }

    [Fact]
    public void ProjectListRefresh_PreservesCurrentSelectionBeforeStoredDefault()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Organization") });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            DefaultOrganizationId = 1,
            DefaultProjectId = 11
        });
        _handler.Respond("api/organizations/1/projects", new[]
        {
            Project(11, 1, "Default"),
            Project(12, 1, "Current")
        });
        var cut = Render<MainLayout>();
        Services.GetRequiredService<ProjectStateService>()
            .SetProject(Project(12, 1, "Current"));

        Services.GetRequiredService<ProjectStateService>().NotifyProjectListChanged();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, _handler.Requests.Count(
                r => r == "api/organizations/1/projects"));
            Assert.Equal(12, Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        });
    }

    [Fact]
    public async Task ProjectChange_WhenSettingsAreNull_PersistsSelectionFromNewSettings()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Organization") });
        _handler.RespondAsync("api/user/settings", HttpMethod.Get, _ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create<UserSettingsDto?>(null)
            }));
        _handler.Respond("api/organizations/1/projects", new[]
        {
            Project(11, 1, "First"),
            Project(12, 1, "Second")
        });
        _handler.RespondStatus("api/user/settings", HttpMethod.Put, HttpStatusCode.NoContent);
        var cut = Render<MainLayout>();
        cut.FindAll("button").Single(button => button.TextContent.Contains("First")).Click();
        var second = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Second"));

        await cut.InvokeAsync(() => second.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() =>
        {
            var saved = Assert.Single(_handler.JsonBodies);
            Assert.Equal(1, saved.DefaultOrganizationId);
            Assert.Equal(12, saved.DefaultProjectId);
        });
    }

    private static OrganizationSummaryDto Organization(int id, string name) =>
        new(id, name, OrganizationRole.Member, false);

    private static Project Project(int id, int organizationId, string name) =>
        new() { ProjectId = id, OrganizationId = organizationId, ProjectName = name };

    private sealed class TestAuthenticationStateProvider(bool authenticated)
        : AuthenticationStateProvider
    {
        private AuthenticationState _state = CreateState(authenticated);

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(_state);

        public void SetAuthenticated(bool authenticated)
        {
            _state = CreateState(authenticated);
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }

        private static AuthenticationState CreateState(bool authenticated)
        {
            var identity = authenticated
                ? new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], "test")
                : new ClaimsIdentity();
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
    }

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<(HttpMethod Method, string Route),
            Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();

        public List<string> Requests { get; } = [];
        public List<UserSettingsDto> JsonBodies { get; } = [];

        public void Respond<T>(string route, T value) where T : notnull =>
            _responses[(HttpMethod.Get, route)] = _ => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });

        public void RespondStatus(string route, HttpMethod method, HttpStatusCode statusCode) =>
            _responses[(method, route)] = _ => Task.FromResult(new HttpResponseMessage(statusCode));

        public void RespondAsync(
            string route,
            Func<CancellationToken, Task<HttpResponseMessage>> response) =>
            _responses[(HttpMethod.Get, route)] = response;

        public void RespondAsync(
            string route,
            HttpMethod method,
            Func<CancellationToken, Task<HttpResponseMessage>> response) =>
            _responses[(method, route)] = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var route = request.RequestUri!.PathAndQuery.TrimStart('/');
            Requests.Add(route);
            if (request.Method == HttpMethod.Put && request.Content is not null)
                JsonBodies.Add((await request.Content.ReadFromJsonAsync<UserSettingsDto>(
                    cancellationToken))!);

            return _responses.TryGetValue((request.Method, route), out var response)
                ? await response(cancellationToken)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
