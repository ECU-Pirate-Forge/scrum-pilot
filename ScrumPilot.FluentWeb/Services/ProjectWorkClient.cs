using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.JSInterop;

namespace ScrumPilot.FluentWeb.Services;

public interface IProjectWorkClient : IAsyncDisposable
{
    string Status { get; }
    Task StartAsync(WorkScope scope, Func<Task> refresh);
    Task StopAsync(WorkScope scope);
}

public sealed class ProjectWorkClient(IJSRuntime js, ILogger<ProjectWorkClient> logger) : IProjectWorkClient
{
    private HubConnection? _connection;
    private CancellationTokenSource? _stop;
    private Task? _poll;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly string _observerId = Guid.NewGuid().ToString("N");
    private DotNetObjectReference<ProjectWorkClient>? _reference;
    private Func<Task>? _refresh;
    private long _generation;
    private WorkScope? _owner;
    public string Status { get; private set; } = "Connecting live updates";

    public async Task StartAsync(WorkScope scope, Func<Task> refresh)
    {
        scope.ThrowIfStale();
        var projectId = scope.ProjectId ?? throw new InvalidOperationException("Project work requires a project scope.");
        var generation = Interlocked.Increment(ref _generation);
        _stop?.Cancel();
        await _transition.WaitAsync();
        try
        {
            await StopCoreAsync();
            if (generation != _generation || !scope.IsCurrent) return;
            _owner = scope;
            Status = "Connecting live updates";
            var stop = _stop = CancellationTokenSource.CreateLinkedTokenSource(scope.Cancellation);
            bool Current() => generation == _generation && !stop.IsCancellationRequested && scope.IsCurrent;
            async Task RefreshCurrentAsync() { if (Current()) await refresh(); }
            var apiBase = scope.Http.BaseAddress ?? throw new InvalidOperationException("API base address is required.");
            var connection = _connection = new HubConnectionBuilder().WithUrl(new Uri(apiBase, OrganizationRoutes.ProjectWorkHub(scope.OrganizationId)), options =>
                options.AccessTokenProvider = async () =>
                {
                    scope.ThrowIfStale();
                    return await js.InvokeAsync<string?>("localStorage.getItem", stop.Token, "authToken");
                })
                .WithAutomaticReconnect().Build();
            _refresh = RefreshCurrentAsync;
            connection.On<int, long>("ProjectWorkChanged", async (changedProject, _) =>
            {
                if (Current() && changedProject == projectId) await RefreshCurrentAsync();
            });
            connection.On<int>("ProjectAccessRevoked", async changedProject =>
            {
                if (Current() && changedProject == projectId) await RefreshCurrentAsync();
            });
            connection.Reconnected += async _ =>
            {
                try
                {
                    if (!Current()) return;
                    await connection.InvokeAsync("Subscribe", projectId, stop.Token);
                    await RefreshCurrentAsync().WaitAsync(stop.Token);
                }
                catch (OperationCanceledException) when (!Current()) { }
                catch (Exception ex) when (ex is HttpRequestException or HubException)
                {
                    if (!Current()) return;
                    logger.LogWarning(ex, "Unable to rejoin project work subscription");
                    Status = "Live subscription unavailable; periodic reconciliation enabled";
                    await RefreshCurrentAsync();
                }
            };
            try
            {
                await connection.StartAsync(stop.Token);
                await connection.InvokeAsync("Subscribe", projectId, stop.Token);
                if (!Current()) return;
                Status = "Live updates connected; periodic reconciliation enabled";
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is HttpRequestException or HubException or InvalidOperationException or JSException)
            {
                if (!Current()) return;
                logger.LogWarning(ex, "Project work live connection unavailable for {ProjectId}", projectId);
                Status = "Live connection unavailable; refreshing every 10 seconds";
            }
            _reference = DotNetObjectReference.Create(this);
            if (!Current()) return;
            try { await js.InvokeVoidAsync("mermaidHelper.observeFocus", _observerId, _reference, generation); }
            catch (JSException ex) { if (Current()) logger.LogWarning(ex, "Focus reconciliation unavailable; periodic reconciliation remains enabled"); }
            if (!Current()) return;
            _poll = ReconcileAsync(RefreshCurrentAsync, stop.Token);
            try { await RefreshCurrentAsync().WaitAsync(stop.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
        finally { _transition.Release(); }
    }

    [JSInvokable]
    public async Task ReconcileOnFocus(long generation)
    {
        if (generation == _generation && _refresh is not null && _stop?.IsCancellationRequested == false) await _refresh();
    }

    private async Task ReconcileAsync(Func<Task> refresh, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct)) await refresh().WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task StopCoreAsync()
    {
        if (_stop is not null) await _stop.CancelAsync();
        if (_poll is not null) await _poll;
        if (_connection is not null) await _connection.DisposeAsync();
        if (_reference is not null)
        {
            try { await js.InvokeVoidAsync("mermaidHelper.unobserveFocus", _observerId); }
            catch (JSException ex) { logger.LogWarning(ex, "Unable to detach project focus reconciliation"); }
            _reference.Dispose();
            _reference = null;
        }
        _stop?.Dispose();
        _stop = null;
        _connection = null;
        _poll = null;
        _refresh = null;
        _owner = null;
    }

    public async Task StopAsync(WorkScope scope)
    {
        await _transition.WaitAsync();
        try
        {
            // A retired page must not dispose the shared client's newer tenant subscription.
            if (ReferenceEquals(_owner, scope)) await StopCoreAsync();
        }
        finally { _transition.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _generation);
        _stop?.Cancel();
        await _transition.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _transition.Release(); }
    }
}
