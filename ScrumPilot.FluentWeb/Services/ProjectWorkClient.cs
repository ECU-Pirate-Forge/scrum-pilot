using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.JSInterop;

namespace ScrumPilot.FluentWeb.Services;

public interface IProjectWorkClient : IAsyncDisposable
{
    string Status { get; }
    Task StartAsync(int projectId, Func<Task> refresh);
}

public sealed class ProjectWorkClient(HttpClient http, IJSRuntime js, ILogger<ProjectWorkClient> logger) : IProjectWorkClient
{
    private HubConnection? _connection;
    private CancellationTokenSource? _stop;
    private Task? _poll;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly string _observerId = Guid.NewGuid().ToString("N");
    private DotNetObjectReference<ProjectWorkClient>? _reference;
    private Func<Task>? _refresh;
    public string Status { get; private set; } = "Connecting live updates";

    public async Task StartAsync(int projectId, Func<Task> refresh)
    {
        _stop?.Cancel();
        await _transition.WaitAsync();
        try
        {
            await StopCoreAsync();
            var stop = _stop = new();
            var connection = _connection = new HubConnectionBuilder().WithUrl(new Uri(http.BaseAddress!, "hubs/project-work"), options =>
                options.AccessTokenProvider = async () => await js.InvokeAsync<string?>("localStorage.getItem", "authToken"))
                .WithAutomaticReconnect().Build();
            _refresh = refresh;
            connection.On<int, long>("ProjectWorkChanged", async (changedProject, _) =>
            {
                if (!stop.IsCancellationRequested && changedProject == projectId) await refresh();
            });
            connection.On<int>("ProjectAccessRevoked", async changedProject =>
            {
                if (!stop.IsCancellationRequested && changedProject == projectId) await refresh();
            });
            connection.Reconnected += async _ =>
            {
                try
                {
                    if (stop.IsCancellationRequested) return;
                    await connection.InvokeAsync("Subscribe", projectId, stop.Token);
                    await refresh();
                }
                catch (Exception ex) when (ex is HttpRequestException or HubException)
                {
                    logger.LogWarning(ex, "Unable to rejoin project work subscription");
                    Status = "Live subscription unavailable; periodic reconciliation enabled";
                    await refresh();
                }
            };
            try
            {
                await connection.StartAsync(stop.Token);
                await connection.InvokeAsync("Subscribe", projectId, stop.Token);
                Status = "Live updates connected; periodic reconciliation enabled";
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is HttpRequestException or HubException or InvalidOperationException or JSException)
            {
                logger.LogWarning(ex, "Project work live connection unavailable for {ProjectId}", projectId);
                Status = "Live connection unavailable; refreshing every 10 seconds";
            }
            _reference = DotNetObjectReference.Create(this);
            try { await js.InvokeVoidAsync("mermaidHelper.observeFocus", _observerId, _reference); }
            catch (JSException ex) { logger.LogWarning(ex, "Focus reconciliation unavailable; periodic reconciliation remains enabled"); }
            _poll = ReconcileAsync(refresh, stop.Token);
            await refresh();
        }
        finally { _transition.Release(); }
    }

    [JSInvokable]
    public async Task ReconcileOnFocus()
    {
        if (_refresh is not null && _stop?.IsCancellationRequested == false) await _refresh();
    }

    private async Task ReconcileAsync(Func<Task> refresh, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct)) await refresh();
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
    }

    public async ValueTask DisposeAsync()
    {
        _stop?.Cancel();
        await _transition.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _transition.Release(); }
    }
}
