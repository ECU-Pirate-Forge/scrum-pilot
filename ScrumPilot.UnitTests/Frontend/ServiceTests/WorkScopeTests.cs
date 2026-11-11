using System.Net;
using System.Net.Http.Json;
using ScrumPilot.Web.Services;
using ScrumPilot.Client;

namespace ScrumPilot.UnitTests.Frontend.ServiceTests;

public sealed class WorkScopeTests
{
    [Fact]
    public void ScopeHasNoVisualServiceDependency()
    {
        Assert.DoesNotContain(typeof(WorkScope).GetProperties(),
            property => property.PropertyType.Name == "MetricsDashboardService");
    }

    [Fact]
    public async Task SameProjectIdInDifferentOrganizationsNeverSharesARequestOrLifetime()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { "current" }) });
        }))
        { BaseAddress = new Uri("https://localhost/") };
        using var first = new WorkScope(client, 1, 7);
        using var second = new WorkScope(client, 2, 7);
        await first.Http.GetFromJsonAsync<string[]>("pbi/getAllPbis?projectId=7");
        first.Dispose();
        await second.Http.GetFromJsonAsync<string[]>("pbi/getAllPbis?projectId=7");
        Assert.Equal(new[]
        {
            "/api/organizations/1/pbi/getAllPbis?projectId=7",
            "/api/organizations/2/pbi/getAllPbis?projectId=7"
        }, requests);
        Assert.True(second.IsCurrent);
        Assert.Equal("https://localhost/", client.BaseAddress!.ToString());
    }

    [Fact]
    public async Task CancelledBoundaryRejectsLateLoadEvenIfTransportIgnoresCancellation()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler((_, _) => delayed.Task)) { BaseAddress = new Uri("https://localhost/") };
        using var work = new WorkScope(client, 1, 1);
        var load = work.Http.GetFromJsonAsync<string[]>("pbi/getAllPbis?projectId=1");
        work.Dispose();
        delayed.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { "stale" }) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
    }

    [Fact]
    public async Task OldEditorAndAiContinuationCannotSendAfterBoundaryDisposal()
    {
        var sent = 0;
        using var client = new HttpClient(new Handler((_, _) =>
        {
            sent++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }))
        { BaseAddress = new Uri("https://localhost/") };
        using var work = new WorkScope(client, 1, 1);
        work.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.Http.PutAsJsonAsync("pbi", new { pbiId = 1 }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.Http.PostAsJsonAsync("pbi/createStories?projectId=1", new[] { new { projectId = 1 } }));
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task BoundaryDisposalCancelsPendingRequests()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { canceled.SetResult(); throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }))
        { BaseAddress = new Uri("https://localhost/") };
        using var work = new WorkScope(client, 1, 1);
        var load = work.Http.GetFromJsonAsync<string[]>("pbi/getAllPbis?projectId=1");
        await started.Task;
        work.Dispose();
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
