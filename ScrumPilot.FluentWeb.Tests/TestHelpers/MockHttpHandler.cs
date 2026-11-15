using System.Net;
using System.Net.Http.Json;

namespace ScrumPilot.FluentWeb.Tests.TestHelpers;

/// <summary>
/// Simple route-based HTTP fake. Register responders per method + path (path without query, e.g. "api/backlog/3").
/// Unmatched requests return 404. Every request (with body text) is recorded in <see cref="Requests"/>.
/// The last matching registration wins, so tests can override earlier registrations.
/// </summary>
public sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, Func<string, bool> Match, Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> Respond)> _routes = [];
    private readonly object _gate = new();

    public List<RecordedRequest> Requests { get; } = [];
    public int DefaultOrganizationId { get; set; } = 1;

    public MockHttpHandler On(HttpMethod method, string path, Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        lock (_gate) _routes.Add((method, p => p == Normalize(path), (request, body) => Task.FromResult(respond(request, body))));
        return this;
    }

    public MockHttpHandler OnAsync(HttpMethod method, string path, Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> respond)
    {
        lock (_gate) _routes.Add((method, p => p == Normalize(path), respond));
        return this;
    }

    public MockHttpHandler OnPrefix(HttpMethod method, string pathPrefix, Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        lock (_gate) _routes.Add((method, p => p.StartsWith(Normalize(pathPrefix), StringComparison.Ordinal), (request, body) => Task.FromResult(respond(request, body))));
        return this;
    }

    public MockHttpHandler OnGetJson<T>(string path, T value) =>
        On(HttpMethod.Get, path, (_, _) => Json(value));

    public MockHttpHandler OnStatus(HttpMethod method, string path, HttpStatusCode status) =>
        On(method, path, (_, _) => new HttpResponseMessage(status));

    public static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    public IReadOnlyList<RecordedRequest> RequestsTo(HttpMethod method, string path)
    {
        var normalized = Normalize(path);
        lock (_gate) return Requests.Where(r => r.Method == method && r.Path == normalized).ToList();
    }

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("https://localhost/") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        // Never translate actual requests: a production call missing the tenant must fail.
        var path = request.RequestUri!.AbsolutePath.Trim('/').ToLowerInvariant();

        Func<HttpRequestMessage, string?, Task<HttpResponseMessage>>? responder = null;
        lock (_gate)
        {
            Requests.Add(new RecordedRequest(request.Method, path, request.RequestUri.Query, body));
            for (var i = _routes.Count - 1; i >= 0; i--)
            {
                if (_routes[i].Method == request.Method && _routes[i].Match(path))
                {
                    responder = _routes[i].Respond;
                    break;
                }
            }
        }

        return responder is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : await responder(request, body);
    }

    private string Normalize(string path)
    {
        path = path.Trim('/').ToLowerInvariant();
        var resource = path.Split('/').ElementAtOrDefault(1);
        if (resource is "pbi" or "project" or "projects" or "sprint" or "epic" or "metrics" or "comments" or "dashboard-preferences"
            || path.StartsWith("api/user/all"))
            return $"api/organizations/{DefaultOrganizationId}/{path[4..]}";
        return path;
    }

    public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body);
}
