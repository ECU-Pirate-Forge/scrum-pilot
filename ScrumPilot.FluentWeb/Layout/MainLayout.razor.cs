using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;
using Icon = Microsoft.FluentUI.AspNetCore.Components.Icon;

namespace ScrumPilot.FluentWeb.Layout;

/// <summary>
/// Application shell. Loads the user's organizations/projects, persists the selection to user settings,
/// applies the theme preference and handles sign-out.
/// </summary>
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
    private readonly object _selectionPersistenceLock = new();
    private Selection? _desiredSelection;
    private Selection? _persistedSelection;
    private Task _selectionWriterTask = Task.CompletedTask;
    private bool _selectionWriterRunning;
    private CancellationTokenSource? _selectionWriterOwner;
    private CancellationTokenSource? _sessionCancellation;
    private int _persistenceGeneration;
    private bool _disposed;

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
        Theme.OnChange += OnThemeChanged;

        var authenticationState = await AuthStateProvider.GetAuthenticationStateAsync();
        if (authenticationState.User.Identity?.IsAuthenticated == true)
            await LoadSessionAsync();
        else
            ClearSession();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _settings is null)
            await Theme.ApplyAsync(Theme.Preference);
    }

    private void OnThemeChanged() => _ = InvokeAsync(StateHasChanged);

    private async Task SetThemeAsync(UiPreference preference)
    {
        await Theme.ApplyAsync(preference);
        if (_settings is null)
            return;

        try
        {
            var latest = await Http.GetFromJsonAsync<UserSettingsDto>("api/user/settings") ?? _settings;
            latest.UiPreference = preference;
            var response = await Http.PutAsJsonAsync("api/user/settings", latest);
            response.EnsureSuccessStatusCode();
            _settings = latest;
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            Notify.ShowWarning("Theme applied, but your preference could not be saved.");
        }
    }

    private void OnAuthStateChanged(Task<AuthenticationState> task)
    {
        _ = InvokeAsync(async () =>
        {
            var state = await task;
            if (state.User.Identity?.IsAuthenticated == true)
                await LoadSessionAsync();
            else
            {
                ClearSession();
                StateHasChanged();
            }
        });
    }

    private void OnProjectListChanged()
    {
        _ = InvokeAsync(async () =>
        {
            var organization = OrganizationState.SelectedOrganization;
            if (organization is not null)
            {
                var result = await LoadProjectsAsync(
                    organization,
                    ProjectState.SelectedProjectId,
                    _settings?.DefaultProjectId,
                    persist: false);
                if (result == ProjectLoadResult.NotFound)
                    await LoadSessionAsync(new HashSet<int> { organization.OrganizationId });
            }
        });
    }

    private async Task LoadSessionAsync(IReadOnlySet<int>? rejectedOrganizationIds = null)
    {
        var generation = ++_loadGeneration;
        ClearSession(incrementGeneration: false);
        CancellationTokenSource sessionCancellation;
        lock (_selectionPersistenceLock)
        {
            if (_disposed)
                return;

            sessionCancellation = new CancellationTokenSource();
            _sessionCancellation = sessionCancellation;
        }

        try
        {
            var organizations = await Http.GetFromJsonAsync<List<OrganizationSummaryDto>>(
                "api/organizations", sessionCancellation.Token) ?? [];
            if (!IsCurrent(generation))
                return;

            _organizations = organizations
                .Where(o => !o.IsDeleted
                    && !(rejectedOrganizationIds?.Contains(o.OrganizationId) ?? false))
                .ToList();

            _settings = await Http.GetFromJsonAsync<UserSettingsDto>(
                "api/user/settings", sessionCancellation.Token);
            if (!IsCurrent(generation))
                return;

            if (_settings is not null)
                await Theme.ApplyAsync(_settings.UiPreference);

            lock (_selectionPersistenceLock)
            {
                _persistedSelection = _settings?.DefaultOrganizationId is int organizationId
                    ? new Selection(organizationId, _settings.DefaultProjectId)
                    : null;
            }

            var candidates = _organizations
                .OrderByDescending(o => o.OrganizationId == _settings?.DefaultOrganizationId)
                .ToList();
            var attemptedOrganizationIds = new HashSet<int>();
            foreach (var organization in candidates)
            {
                if (!attemptedOrganizationIds.Add(organization.OrganizationId)
                    || !IsCurrent(generation))
                    continue;

                OrganizationState.SetOrganization(organization);
                var result = await LoadProjectsAsync(
                    organization,
                    _settings?.DefaultProjectId,
                    fallbackProjectId: null,
                    persist: false,
                    generation);
                if (result == ProjectLoadResult.Success || result == ProjectLoadResult.Failed)
                    return;
            }

            if ((attemptedOrganizationIds.Count > 0 || rejectedOrganizationIds?.Count > 0)
                && IsCurrent(generation))
            {
                ClearSession(incrementGeneration: false);
                Notify.ShowError("Unable to load organizations and projects.");
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (IsCurrent(generation))
                ClearSession();
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
            // Session was replaced or cleared; nothing to report.
            return;
        }
        catch (Exception)
        {
            if (IsCurrent(generation))
            {
                ClearSession();
                Notify.ShowError("Unable to load organizations and projects.");
            }
        }
        finally
        {
            if (IsCurrent(generation))
                StateHasChanged();
        }
    }

    private async Task SelectOrganizationAsync(OrganizationSummaryDto organization)
    {
        if (!_organizations.Any(o => o.OrganizationId == organization.OrganizationId))
            return;

        OrganizationState.SetOrganization(organization);
        _appBarProjects = [];
        ProjectState.Clear();
        StateHasChanged();
        var result = await LoadProjectsAsync(
            organization,
            _settings?.DefaultProjectId,
            fallbackProjectId: null,
            persist: true);
        if (result == ProjectLoadResult.NotFound)
            await LoadSessionAsync(new HashSet<int> { organization.OrganizationId });
    }

    private async Task<ProjectLoadResult> LoadProjectsAsync(
        OrganizationSummaryDto organization,
        int? preferredProjectId,
        int? fallbackProjectId,
        bool persist,
        int? existingGeneration = null)
    {
        var generation = existingGeneration ?? ++_loadGeneration;
        CancellationTokenSource? sessionCancellation;
        lock (_selectionPersistenceLock)
            sessionCancellation = _sessionCancellation;
        if (sessionCancellation is null || sessionCancellation.IsCancellationRequested)
            return ProjectLoadResult.Failed;

        try
        {
            var projects = await Http.GetFromJsonAsync<List<Project>>(
                $"api/organizations/{organization.OrganizationId}/projects",
                sessionCancellation.Token) ?? [];
            if (!IsCurrent(generation)
                || OrganizationState.SelectedOrganizationId != organization.OrganizationId)
                return ProjectLoadResult.Failed;

            _appBarProjects = projects;
            var selected = preferredProjectId is int projectId
                ? projects.FirstOrDefault(p => p.ProjectId == projectId)
                : null;
            selected ??= fallbackProjectId is int fallbackId
                ? projects.FirstOrDefault(p => p.ProjectId == fallbackId)
                : null;
            selected ??= projects.FirstOrDefault();
            ProjectState.SetProject(selected);

            if (persist)
                await PersistLatestSelectionAsync(organization.OrganizationId, selected?.ProjectId);
            return ProjectLoadResult.Success;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            if (IsCurrent(generation))
            {
                OrganizationState.Clear();
                _appBarProjects = [];
                ProjectState.Clear();
            }
            return ProjectLoadResult.NotFound;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (IsCurrent(generation))
                ClearSession();
            return ProjectLoadResult.Failed;
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
            return ProjectLoadResult.Failed;
        }
        catch (Exception)
        {
            if (IsCurrent(generation))
            {
                _appBarProjects = [];
                ProjectState.Clear();
                Notify.ShowError("Unable to load projects.");
            }
            return ProjectLoadResult.Failed;
        }
        finally
        {
            if (IsCurrent(generation))
                StateHasChanged();
        }
    }

    private async Task SetGlobalProject(Project project)
    {
        if (!_appBarProjects.Any(p => p.ProjectId == project.ProjectId))
            return;

        _loadGeneration++;
        ProjectState.SetProject(project);
        if (OrganizationState.SelectedOrganizationId is int organizationId)
            await PersistLatestSelectionAsync(organizationId, project.ProjectId);
    }

    private Task PersistLatestSelectionAsync(int organizationId, int? projectId)
    {
        lock (_selectionPersistenceLock)
        {
            var sessionCancellation = _sessionCancellation;
            if (_disposed || sessionCancellation is null
                || sessionCancellation.IsCancellationRequested)
                return Task.CompletedTask;

            _desiredSelection = new Selection(organizationId, projectId);
            if (!_selectionWriterRunning
                || !ReferenceEquals(_selectionWriterOwner, sessionCancellation))
            {
                _selectionWriterRunning = true;
                _selectionWriterOwner = sessionCancellation;
                _selectionWriterTask = RunSelectionWriterAsync(
                    _persistenceGeneration, sessionCancellation);
            }

            return _selectionWriterTask;
        }
    }

    private async Task RunSelectionWriterAsync(
        int persistenceGeneration,
        CancellationTokenSource sessionCancellation)
    {
        try
        {
            while (true)
            {
                Selection target;
                lock (_selectionPersistenceLock)
                {
                    if (!IsCurrentWriter(persistenceGeneration, sessionCancellation)
                        || _desiredSelection is not Selection desired
                        || desired == _persistedSelection)
                        return;

                    target = desired;
                }

                try
                {
                    var latest = await Http.GetFromJsonAsync<UserSettingsDto>(
                        "api/user/settings", sessionCancellation.Token)
                        ?? _settings
                        ?? new UserSettingsDto();
                    lock (_selectionPersistenceLock)
                    {
                        if (!IsCurrentWriter(persistenceGeneration, sessionCancellation))
                            return;
                        if (_desiredSelection is Selection latestDesired)
                            target = latestDesired;
                    }

                    var merged = new UserSettingsDto
                    {
                        Email = latest.Email,
                        DiscordUsername = latest.DiscordUsername,
                        UiPreference = latest.UiPreference,
                        DefaultOrganizationId = target.OrganizationId,
                        DefaultProjectId = target.ProjectId
                    };

                    var response = await Http.PutAsJsonAsync(
                        "api/user/settings", merged, sessionCancellation.Token);
                    response.EnsureSuccessStatusCode();

                    lock (_selectionPersistenceLock)
                    {
                        if (!IsCurrentWriter(persistenceGeneration, sessionCancellation))
                            return;

                        _persistedSelection = target;
                        _settings = merged;
                    }
                }
                catch (OperationCanceledException)
                    when (sessionCancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    bool retryLatest;
                    lock (_selectionPersistenceLock)
                    {
                        if (!IsCurrentWriter(persistenceGeneration, sessionCancellation))
                            return;
                        retryLatest = _desiredSelection is Selection desired && desired != target;
                    }

                    Notify.ShowError("Unable to save your organization and project selection.");
                    if (!retryLatest)
                        return;
                }
            }
        }
        finally
        {
            lock (_selectionPersistenceLock)
            {
                if (ReferenceEquals(_selectionWriterOwner, sessionCancellation))
                {
                    _selectionWriterRunning = false;
                    _selectionWriterOwner = null;
                }
            }
        }
    }

    private bool IsCurrentWriter(int persistenceGeneration, CancellationTokenSource sessionCancellation) =>
        !_disposed
        && persistenceGeneration == _persistenceGeneration
        && ReferenceEquals(_sessionCancellation, sessionCancellation)
        && !sessionCancellation.IsCancellationRequested;

    private bool IsCurrent(int generation) => !_disposed && generation == _loadGeneration;

    private void ClearSession(bool incrementGeneration = true)
    {
        if (incrementGeneration)
            _loadGeneration++;
        _organizations = [];
        _appBarProjects = [];
        _settings = null;
        CancellationTokenSource? sessionCancellation;
        lock (_selectionPersistenceLock)
        {
            _persistenceGeneration++;
            _desiredSelection = null;
            _persistedSelection = null;
            sessionCancellation = _sessionCancellation;
            _sessionCancellation = null;
        }
        sessionCancellation?.Cancel();
        sessionCancellation?.Dispose();
        OrganizationState.Clear();
        ProjectState.Clear();
    }

    private async Task HandleLogout()
    {
        var confirmed = await Dialogs.ConfirmAsync(
            "Sign Out",
            "Are you sure you want to sign out?",
            confirmLabel: "Sign Out",
            cancelLabel: "Cancel");

        if (confirmed)
        {
            ClearSession();
            await AuthService.LogoutAsync();
            Navigation.NavigateTo("/login");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _loadGeneration++;
        CancellationTokenSource? sessionCancellation;
        lock (_selectionPersistenceLock)
        {
            _persistenceGeneration++;
            _desiredSelection = null;
            _persistedSelection = null;
            sessionCancellation = _sessionCancellation;
            _sessionCancellation = null;
        }
        sessionCancellation?.Cancel();
        sessionCancellation?.Dispose();
        AuthStateProvider.AuthenticationStateChanged -= OnAuthStateChanged;
        ProjectState.OnProjectListChanged -= OnProjectListChanged;
        Theme.OnChange -= OnThemeChanged;
    }

    private readonly record struct Selection(int OrganizationId, int? ProjectId);

    private enum ProjectLoadResult
    {
        Success,
        NotFound,
        Failed
    }
}
