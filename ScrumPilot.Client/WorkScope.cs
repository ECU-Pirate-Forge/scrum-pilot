using System.Net.Http.Json;

namespace ScrumPilot.Client;

/// <summary>
/// Immutable lifetime of one routed organization/project view. Never registered as mutable
/// application-wide state: every boundary owns and cancels its own scope.
/// </summary>
public sealed class WorkScope : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    public int OrganizationId { get; }
    public int? ProjectId { get; }
    public CancellationToken Cancellation { get; }
    public WorkHttpClient Http { get; }
    public bool IsCurrent => !Cancellation.IsCancellationRequested;

    public WorkScope(HttpClient http, int organizationId, int? projectId)
    {
        // Validate even when this view has not issued a request yet.
        _ = OrganizationRoutes.Api(organizationId, "project");
        OrganizationId = organizationId;
        ProjectId = projectId;
        Cancellation = _lifetime.Token;
        Http = new WorkHttpClient(http, this);
    }

    public void ThrowIfStale() => Cancellation.ThrowIfCancellationRequested();

    public void Dispose()
    {
        if (!IsCurrent) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

/// <summary>
/// Requests capture a tenant at construction, not from a global header/base address.
/// Checks after deserialization also reject handlers that ignore cancellation.
/// </summary>
public sealed class WorkHttpClient(HttpClient client, WorkScope scope)
{
    public Uri? BaseAddress => client.BaseAddress;
    public async Task<T?> GetFromJsonAsync<T>(string suffix, CancellationToken cancellationToken = default)
    {
        scope.ThrowIfStale();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(scope.Cancellation, cancellationToken);
        var value = await client.GetFromJsonAsync<T>(OrganizationRoutes.Api(scope.OrganizationId, suffix), linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        return value;
    }

    public Task<HttpResponseMessage> PostAsJsonAsync<T>(string suffix, T value, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, suffix, JsonContent.Create(value), cancellationToken);

    public Task<HttpResponseMessage> PutAsJsonAsync<T>(string suffix, T value, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, suffix, JsonContent.Create(value), cancellationToken);

    public Task<HttpResponseMessage> PostAsync(string suffix, HttpContent? content, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, suffix, content, cancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(string suffix, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, suffix, null, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string suffix, HttpContent? content, CancellationToken cancellationToken)
    {
        scope.ThrowIfStale();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(scope.Cancellation, cancellationToken);
        using var request = new HttpRequestMessage(method, OrganizationRoutes.Api(scope.OrganizationId, suffix)) { Content = content };
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
        if (linked.IsCancellationRequested)
        {
            response.Dispose();
            linked.Token.ThrowIfCancellationRequested();
        }
        return response;
    }
}
