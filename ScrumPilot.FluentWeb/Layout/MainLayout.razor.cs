using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;
using Icon = Microsoft.FluentUI.AspNetCore.Components.Icon;

namespace ScrumPilot.FluentWeb.Layout;

/// <summary>Tab-local, URL-authoritative work boundary. Only settings explicitly save defaults.</summary>
public partial class MainLayout
{
    [Inject] private IAuthService AuthService { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IAppDialogService Dialogs { get; set; } = default!;
    [Inject] private IAppNotificationService Notify { get; set; } = default!;
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private OrganizationStateService OrganizationState { get; set; } = default!;
    [Inject] private ProjectStateService ProjectState { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private AppThemeService Theme { get; set; } = default!;

    private ErrorBoundary? _errorBoundary;
    private List<OrganizationSummaryDto> _organizations = [];
    private List<Project> _appBarProjects = [];
    private UserSettingsDto? _settings;
    private int _loadGeneration;
    private CancellationTokenSource? _sessionCancellation;
    private bool _disposed;
    private bool _authenticated;
    private bool _applyingRoute;
    private bool _routeLoading = true;
    private bool _needsOrganization;
    private string? _routeError;
    private WorkScope? _work;

    private Icon ThemeIcon => Theme.Preference switch
    {
        UiPreference.Dark => new Icons.Regular.Size20.WeatherMoon(),
        UiPreference.Light => new Icons.Regular.Size20.WeatherSunny(),
        _ => new Icons.Regular.Size20.DarkTheme(),
    };

    protected override async Task OnInitializedAsync()
    {
        AuthStateProvider.AuthenticationStateChanged += OnAuthStateChanged;
        ProjectState.OnProjectListChanged += OnProjectListChanged;
        ProjectState.OnChange += OnProjectSelectionChanged;
        Navigation.LocationChanged += OnLocationChanged;
        Theme.OnChange += OnThemeChanged;
        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        _authenticated = state.User.Identity?.IsAuthenticated == true;
        if (_authenticated) await LoadSessionAsync();
        else ClearSession();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _settings is null)
            await Theme.ApplyAsync(Theme.Preference);
    }

    private void OnThemeChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task SetThemeAsync(UiPreference preference)
    {
        // Fluent radios also emit CheckedChanged when their initial value hydrates.
        if (preference == Theme.Preference) return;
        await Theme.ApplyAsync(preference);
        var cancellation = _sessionCancellation;
        if (_settings is null || cancellation is null) return;
        var generation = _loadGeneration;
        try
        {
            // Merge with the current account settings, never with tab-local work selection.
            var latest = await Http.GetFromJsonAsync<UserSettingsDto>("api/user/settings", cancellation.Token) ?? _settings;
            if (!IsCurrent(generation)) return;
            latest.UiPreference = preference;
            using var response = await Http.PutAsJsonAsync("api/user/settings", latest, cancellation.Token);
            response.EnsureSuccessStatusCode();
            if (IsCurrent(generation)) _settings = latest;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            if (IsCurrent(generation)) Notify.ShowWarning("Theme applied, but your preference could not be saved.");
        }
    }

    private void OnAuthStateChanged(Task<AuthenticationState> task) => _ = InvokeAsync(async () =>
    {
        var state = await task;
        _authenticated = state.User.Identity?.IsAuthenticated == true;
        if (_authenticated) await LoadSessionAsync();
        else { ClearSession(); StateHasChanged(); }
    });

    private void OnProjectListChanged() => _ = InvokeAsync(LoadSessionAsync);

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        _work?.Dispose();
        _errorBoundary?.Recover();
        _ = InvokeAsync(LoadSessionAsync);
    }

    private void OnProjectSelectionChanged()
    {
        if (_applyingRoute || _disposed || _work is not { IsCurrent: true }) return;
        var uri = new Uri(Navigation.Uri);
        if (OrganizationRoutes.ProjectId(uri) != ProjectState.SelectedProjectId)
            Navigation.NavigateTo(OrganizationRoutes.Page(_work.OrganizationId, OrganizationRoutes.CurrentPage(uri), ProjectState.SelectedProjectId));
    }

    private async Task LoadSessionAsync()
    {
        if (_disposed || !_authenticated) return;
        var generation = ++_loadGeneration;
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _sessionCancellation = cancellation;
        _work?.Dispose();
        _work = null;
        _routeLoading = true;
        _routeError = null;
        _needsOrganization = false;
        _appBarProjects = [];
        _applyingRoute = true;
        OrganizationState.Clear();
        ProjectState.Clear();
        _applyingRoute = false;
        var uri = new Uri(Navigation.Uri);
        var organizationId = OrganizationRoutes.OrganizationId(uri);
        var projectId = OrganizationRoutes.ProjectId(uri);
        var isLanding = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', 2)[0].Length == 0;
        var redirecting = false;
        try
        {
            var organizations = await Http.GetFromJsonAsync<List<OrganizationSummaryDto>>("api/organizations", cancellation.Token) ?? [];
            if (!IsCurrent(generation)) return;
            _organizations = organizations.Where(o => !o.IsDeleted).ToList();
            if (_settings is null || (isLanding && string.IsNullOrEmpty(uri.Query)))
            {
                var settings = await Http.GetFromJsonAsync<UserSettingsDto>("api/user/settings", cancellation.Token);
                if (!IsCurrent(generation)) return;
                _settings = settings;
                if (settings is not null) await Theme.ApplyAsync(settings.UiPreference);
                if (!IsCurrent(generation)) return;
            }
            if (isLanding && string.IsNullOrEmpty(uri.Query) && _settings?.DefaultOrganizationId is int defaultId)
            {
                Navigation.NavigateTo(OrganizationRoutes.Page(defaultId, "backlog", _settings.DefaultProjectId), replace: true);
                redirecting = true;
                return;
            }
            if (organizationId is null)
            {
                _needsOrganization = OrganizationRoutes.IsLegacyWorkLink(uri) || (isLanding && projectId.HasValue);
                return;
            }
            var organization = _organizations.FirstOrDefault(o => o.OrganizationId == organizationId);
            if (organization is null)
            {
                _routeError = "This organization is unavailable, deleted, or you do not have membership.";
                return;
            }
            // Admin directory visibility never substitutes for work membership.
            var projects = await Http.GetFromJsonAsync<List<Project>>(OrganizationRoutes.Api(organizationId.Value, "projects"), cancellation.Token) ?? [];
            if (!IsCurrent(generation)) return;
            var selected = projects.FirstOrDefault(p => p.ProjectId == projectId);
            if (projectId.HasValue && selected is null)
            {
                _routeError = "This project is unavailable or you do not have project access in this organization.";
                return;
            }
            _appBarProjects = projects;
            _applyingRoute = true;
            OrganizationState.SetOrganization(organization);
            ProjectState.SetProject(selected);
            _applyingRoute = false;
            _work = new WorkScope(Http, organizationId.Value, projectId);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (HttpRequestException)
        {
            if (IsCurrent(generation))
                _routeError = "Unable to open this organization. It may be provisioning, unavailable, or your access may have changed.";
        }
        finally
        {
            if (IsCurrent(generation) && !redirecting) { _routeLoading = false; StateHasChanged(); }
        }
    }

    private Task SelectOrganizationAsync(OrganizationSummaryDto organization)
    {
        var uri = new Uri(Navigation.Uri);
        // Rehydrating the selected radio is not a tenant switch: retain the URL's project.
        if (OrganizationRoutes.OrganizationId(uri) == organization.OrganizationId) return Task.CompletedTask;
        Navigation.NavigateTo(OrganizationRoutes.Page(organization.OrganizationId,
            OrganizationRoutes.IsWorkPage(uri) ? OrganizationRoutes.CurrentPage(uri) : "backlog"));
        return Task.CompletedTask;
    }

    private Task SetGlobalProject(Project project)
    {
        var uri = new Uri(Navigation.Uri);
        if (_work is not null && OrganizationRoutes.OrganizationId(uri) == _work.OrganizationId
            && OrganizationRoutes.ProjectId(uri) == project.ProjectId) return Task.CompletedTask;
        if (_work is { IsCurrent: true } && _appBarProjects.Any(p => p.ProjectId == project.ProjectId))
            Navigation.NavigateTo(OrganizationRoutes.Page(_work.OrganizationId, OrganizationRoutes.CurrentPage(uri), project.ProjectId));
        return Task.CompletedTask;
    }

    private bool IsCurrent(int generation) => !_disposed && generation == _loadGeneration;

    private void ClearSession()
    {
        _loadGeneration++;
        _work?.Dispose();
        _work = null;
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _organizations = [];
        _appBarProjects = [];
        _settings = null;
        _routeLoading = false;
        _needsOrganization = false;
        _routeError = null;
        _applyingRoute = true;
        OrganizationState.Clear();
        ProjectState.Clear();
        _applyingRoute = false;
    }

    private async Task HandleLogout()
    {
        if (!await Dialogs.ConfirmAsync("Sign Out", "Are you sure you want to sign out?", "Sign Out", "Cancel")) return;
        ClearSession();
        await AuthService.LogoutAsync();
        Navigation.NavigateTo("login");
    }

    public void Dispose()
    {
        _disposed = true;
        ClearSession();
        AuthStateProvider.AuthenticationStateChanged -= OnAuthStateChanged;
        ProjectState.OnProjectListChanged -= OnProjectListChanged;
        ProjectState.OnChange -= OnProjectSelectionChanged;
        Navigation.LocationChanged -= OnLocationChanged;
        Theme.OnChange -= OnThemeChanged;
    }
}
