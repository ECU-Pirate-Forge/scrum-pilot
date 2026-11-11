using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Layout;
using ScrumPilot.Web.Services;
using ScrumPilot.Client;

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
        Services.GetRequiredService<NavigationManager>().NavigateTo("/organizations/1/backlog?projectId=11");
    }

    [Fact]
    public async Task IndependentTabsKeepTheirOwnScopeWithoutWritingDefaults()
    {
        await using var otherTab = new MainLayoutTests();
        foreach (var tab in new[] { this, otherTab })
        {
            tab._handler.Respond("api/organizations", new[] { Organization(1, "First"), Organization(2, "Second") });
            tab._handler.Respond("api/user/settings", new UserSettingsDto { DefaultOrganizationId = 2, DefaultProjectId = 11 });
            tab._handler.Respond("api/organizations/1/projects", new[] { Project(11, 1, "First project 11") });
            tab._handler.Respond("api/organizations/2/projects", new[] { Project(11, 2, "Second project 11") });
        }
        otherTab.Services.GetRequiredService<NavigationManager>().NavigateTo("/organizations/2/backlog?projectId=11");
        using var first = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<WorkProbe>(0);
            builder.CloseComponent();
        }));
        using var second = otherTab.Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<WorkProbe>(0);
            builder.CloseComponent();
        }));
        var secondScope = second.FindComponent<WorkProbe>().Instance.Work;

        await first.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/organizations/2/backlog?projectId=11"));

        Assert.True(secondScope.IsCurrent);
        Assert.Same(secondScope, second.FindComponent<WorkProbe>().Instance.Work);
        Assert.Equal(2, otherTab.Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
        Assert.Empty(_handler.JsonBodies);
        Assert.Empty(otherTab._handler.JsonBodies);
    }

    [Fact]
    public async Task ExplicitRouteWinsOverDefaultsAndSameLocalProjectIdsRemountTheBoundary()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "First"), Organization(2, "Second") });
        _handler.Respond("api/user/settings", new UserSettingsDto { DefaultOrganizationId = 2, DefaultProjectId = 11 });
        _handler.Respond("api/organizations/1/projects", new[] { Project(11, 1, "First project 11") });
        _handler.Respond("api/organizations/2/projects", new[] { Project(11, 2, "Second project 11") });
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, builder =>
        {
            builder.OpenComponent<WorkProbe>(0);
            builder.CloseComponent();
        }));
        var old = cut.FindComponent<WorkProbe>().Instance;
        Assert.Equal(1, old.Work.OrganizationId);
        Assert.Equal(11, old.Work.ProjectId);

        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>()
            .NavigateTo("/organizations/2/backlog?projectId=11"));

        cut.WaitForAssertion(() =>
        {
            var current = cut.FindComponent<WorkProbe>().Instance;
            Assert.NotSame(old, current);
            Assert.False(old.Work.IsCurrent);
            Assert.Equal(2, current.Work.OrganizationId);
            Assert.Equal(11, current.Work.ProjectId);
            Assert.Equal(2, Services.GetRequiredService<ProjectStateService>().SelectedProject!.OrganizationId);
        });
        Assert.Empty(_handler.JsonBodies);
    }

    [Fact]
    public void LegacyProjectOnlyLinkShowsPickerAndNeverUsesDefaultsToGuessTenant()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/backlog?projectId=11");
        _handler.Respond("api/organizations", new[] { Organization(1, "First") });
        _handler.Respond("api/user/settings", new UserSettingsDto { DefaultOrganizationId = 1, DefaultProjectId = 11 });
        var cut = Render<MainLayout>();
        Assert.Contains("Old project-only links cannot identify an organization", cut.Markup);
        Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
        Assert.DoesNotContain(_handler.Requests, path => path.EndsWith("/projects"));
    }

    public sealed class WorkProbe : ComponentBase
    {
        [CascadingParameter] public WorkScope Work { get; set; } = default!;
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            builder.AddContent(0, $"{Work.OrganizationId}/{Work.ProjectId}");
    }

    [Fact]
    public async Task AnonymousGlobalNavigationDoesNotLoadTenantDataOrHideLogin()
    {
        _authentication.SetAuthenticated(false);
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body,
            builder => builder.AddContent(0, "Global login content")));
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/login"));
        Assert.Empty(_handler.Requests);
        Assert.Contains("Global login content", cut.Markup);
        Assert.DoesNotContain("Unable to open", cut.Markup);
    }

    [Fact]
    public void AdminWithoutMembershipCannotOpenWorkAndNeverFallsBack()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Directory only"), Organization(2, "Other") });
        _handler.Respond("api/user/settings", new UserSettingsDto { DefaultOrganizationId = 2 });
        _handler.RespondStatus("api/organizations/1/projects", HttpMethod.Get, HttpStatusCode.Forbidden);
        var cut = Render<MainLayout>();
        Assert.Contains("Unable to open this organization.", cut.Markup);
        Assert.DoesNotContain("api/organizations/2/projects", _handler.Requests);
        Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
    }

    [Fact]
    public void AuthenticatedStartup_SelectsAccessibleDefaultsAndUsesOrganizationRoute()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/");
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Default")
        });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            Email = "user@example.com",
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
    public void Startup_ShowsUnavailableDefaultWithoutFallingBackToAnotherOrganization()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/");
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

        Assert.Null(Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
        Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        Assert.Contains("api/organizations", _handler.Requests);
        Assert.Contains("unavailable", cut.Markup);
        Assert.DoesNotContain("api/organizations/4/projects", _handler.Requests);
    }

    [Fact]
    public async Task OrganizationChange_ClearsProjectAndDoesNotPersistAccountDefaults()
    {
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Second")
        });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            Email = "keep@example.com",
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
        cut.WaitForAssertion(() =>
            Assert.Equal(2, Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId));
        Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        Assert.Empty(_handler.JsonBodies);
        Assert.EndsWith("/organizations/2/backlog", Services.GetRequiredService<NavigationManager>().Uri);
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
        cut.FindAll("button").Single(button => button.TextContent.Contains("Select Organization")).Click();
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
            Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        });
    }

    [Fact]
    public async Task ProjectChange_UpdatesOnlyTheRouteNotAccountSettings()
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
                    UiPreference = UiPreference.Light,
                    DefaultOrganizationId = 1,
                    DefaultProjectId = 11
                }
                : new UserSettingsDto
                {
                    Email = "latest@example.com",
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

        Assert.Empty(_handler.JsonBodies);
        Assert.Equal(1, settingsRequest);
        Assert.Equal(12, Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        Assert.EndsWith("/organizations/1/backlog?projectId=12", Services.GetRequiredService<NavigationManager>().Uri);
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
            Assert.Contains("Unable to open this organization.", cut.Markup);
        });
    }

    [Fact]
    public async Task Startup_WhenExplicitOrganizationIsStale_NeverAttemptsAnother()
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
            Assert.Contains("Unable to open this organization.", cut.Markup);
            Assert.Equal(1, _handler.Requests.Count(r => r == "api/organizations/1/projects"));
            Assert.DoesNotContain("api/organizations/2/projects", _handler.Requests);
        });
        var requestCount = _handler.Requests.Count;

        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(requestCount, _handler.Requests.Count);
    }

    [Fact]
    public async Task RapidProjectChanges_UseLatestUrlWithoutWritingDefaults()
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
        Assert.Empty(_handler.JsonBodies);

        Task thirdSelection = Task.CompletedTask;
        var thirdDispatch = cut.InvokeAsync(() =>
        {
            thirdSelection = third.Instance.OnClick.InvokeAsync(new MouseEventArgs());
        });
        cut.WaitForAssertion(() =>
            Assert.Equal(13, Services.GetRequiredService<ProjectStateService>().SelectedProjectId));
        Assert.Empty(_handler.JsonBodies);

        firstPut.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await Task.WhenAll(secondSelection, thirdDispatch);
        await thirdSelection;

        Assert.Empty(_handler.JsonBodies);
        Assert.Equal(0, maximumActivePuts);
        Assert.EndsWith("projectId=13", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task Logout_AfterSwitchNeverStartsAPreferenceWrite()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Organization") });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            DefaultOrganizationId = 1,
            DefaultProjectId = 11
        });
        _handler.Respond("api/organizations/1/projects", new[]
        {
            Project(11, 1, "First"),
            Project(12, 1, "Second")
        });
        var putStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var putCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedPuts = 0;
        _handler.RespondAsync("api/user/settings", HttpMethod.Put, async token =>
        {
            putStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                Interlocked.Increment(ref completedPuts);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            catch (OperationCanceledException)
            {
                putCanceled.TrySetResult();
                throw;
            }
        });
        var cut = Render<MainLayout>();
        cut.FindAll("button").Single(button => button.TextContent.Contains("First")).Click();
        var second = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Second"));

        var selectionTask = cut.InvokeAsync(
            () => second.Instance.OnClick.InvokeAsync(new MouseEventArgs()));
        await selectionTask;
        Assert.False(putStarted.Task.IsCompleted);
        cut.Find("[title='Sign out']").Click();
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Sign Out").Click();

        Assert.False(putCanceled.Task.IsCompleted);
        Assert.Equal(0, completedPuts);
        Assert.DoesNotContain(
            "Unable to save your organization and project selection.", cut.Markup);
    }

    [Fact]
    public async Task Dispose_AfterSwitchNeverStartsAPreferenceWrite()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Organization") });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            DefaultOrganizationId = 1,
            DefaultProjectId = 11
        });
        _handler.Respond("api/organizations/1/projects", new[]
        {
            Project(11, 1, "First"),
            Project(12, 1, "Second")
        });
        var putStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var putCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.RespondAsync("api/user/settings", HttpMethod.Put, async token =>
        {
            putStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            catch (OperationCanceledException)
            {
                putCanceled.TrySetResult();
                throw;
            }
        });
        var cut = Render<MainLayout>();
        cut.FindAll("button").Single(button => button.TextContent.Contains("First")).Click();
        var second = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Second"));

        var selectionTask = cut.InvokeAsync(
            () => second.Instance.OnClick.InvokeAsync(new MouseEventArgs()));
        await selectionTask;
        Assert.False(putStarted.Task.IsCompleted);
        cut.Instance.Dispose();

        Assert.False(putCanceled.Task.IsCompleted);
        Assert.Empty(_handler.JsonBodies);
    }

    [Fact]
    public async Task NewSessionSelection_UsesUrlWithoutAnySettingsWriter()
    {
        _handler.Respond("api/organizations", new[] { Organization(1, "Organization") });
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            DefaultOrganizationId = 1,
            DefaultProjectId = 11
        });
        _handler.Respond("api/organizations/1/projects", new[]
        {
            Project(11, 1, "First"),
            Project(12, 1, "Second"),
            Project(13, 1, "Third")
        });
        var stalePutStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStalePut = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var putCount = 0;
        _handler.RespondAsync("api/user/settings", HttpMethod.Put, _ =>
        {
            var request = Interlocked.Increment(ref putCount);
            if (request == 1)
            {
                stalePutStarted.TrySetResult();
                return releaseStalePut.Task;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var cut = Render<MainLayout>();
        cut.FindAll("button").Single(button => button.TextContent.Contains("First")).Click();
        var second = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Second"));
        var staleSelection = cut.InvokeAsync(
            () => second.Instance.OnClick.InvokeAsync(new MouseEventArgs()));
        await staleSelection;
        Assert.False(stalePutStarted.Task.IsCompleted);

        _authentication.SetAuthenticated(false);
        cut.WaitForAssertion(() =>
            Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProject));
        _authentication.SetAuthenticated(true);
        cut.WaitForAssertion(() =>
            Assert.Equal(12, Services.GetRequiredService<ProjectStateService>().SelectedProjectId));
        cut.FindAll("button").Single(button => button.TextContent.Contains("Second")).Click();
        var third = cut.FindComponents<MudMenuItem>()
            .Single(item => item.Markup.Contains("Third"));

        await cut.InvokeAsync(
            () => third.Instance.OnClick.InvokeAsync(new MouseEventArgs()))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, putCount);

        releaseStalePut.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await staleSelection.WaitAsync(TimeSpan.FromSeconds(5));
        await cut.InvokeAsync(
            () => third.Instance.OnClick.InvokeAsync(new MouseEventArgs()));

        Assert.Equal(0, putCount);
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
            Assert.True(_handler.Requests.Count(
                r => r == "api/organizations/1/projects") >= 2);
            Assert.Equal(12, Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
        });
    }

    [Fact]
    public async Task ProjectChange_WhenSettingsAreNull_StillNeverPersistsDefaults()
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
            Assert.Empty(_handler.JsonBodies);
            Assert.Equal(12, Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
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
