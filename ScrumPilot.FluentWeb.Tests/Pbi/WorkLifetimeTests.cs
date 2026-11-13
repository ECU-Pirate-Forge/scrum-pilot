using System.Net;
using System.Net.Http.Json;
using ApexCharts;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;
using ScrumPilot.Client;
using ScrumPilot.FluentWeb.Components.MetricsDashboard;
using ScrumPilot.FluentWeb.Components.Pbi;
using ScrumPilot.FluentWeb.Pages;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Pbi;

public sealed class WorkLifetimeTests : FluentFrontendTestBase
{
    [Theory]
    [InlineData("backlog", "Sprint")]
    [InlineData("backlog", "Pbi/getNonDraftPbis")]
    [InlineData("scrum-board", "Sprint")]
    [InlineData("scrum-board", "Pbi/getNonDraftPbis")]
    [InlineData("dependency-chart", "Pbi/getNonDraftPbis")]
    [InlineData("backlog", "Sprint", false)]
    [InlineData("scrum-board", "Sprint", false)]
    [InlineData("scrum-board", "Pbi/getNonDraftPbis", false)]
    [InlineData("dependency-chart", "Pbi/getNonDraftPbis", false)]
    [InlineData("backlog", "Pbi/getNonDraftPbis", true, "success")]
    [InlineData("scrum-board", "Sprint", true, "failure")]
    public async Task Page_load_reports_only_current_scope_failures(
        string page, string delayedResource, bool superseded = true, string completion = "cancel")
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = Substitute.For<IAppNotificationService>();
        using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.Services.AddLogging();
        context.Services.AddSingleton(notifications);
        context.Services.AddScoped<ProjectStateService>();
        context.Services.AddScoped<AppThemeService>();
        context.Services.AddScoped<IAppDialogService, AppDialogService>();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/" + delayedResource, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Json(Array.Empty<object>()));
            started.TrySetResult();
            return delayed.Task;
        }))
        { BaseAddress = new Uri("https://localhost/") };
        using var work = new WorkScope(client, 1, 7);
        using var cut = context.Render<LoadTestPage>(p => p.Add(x => x.Work, work).Add(x => x.Page, page));
        cut.WaitForState(() => started.Task.IsCompleted);

        if (superseded)
        {
            work.Dispose();
            if (completion == "success") delayed.SetResult(Json(Array.Empty<object>()));
            else if (completion == "failure") delayed.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            else delayed.SetCanceled(work.Cancellation);
        }
        else
        {
            delayed.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading", cut.Markup));

        await cut.InvokeAsync(() => Task.CompletedTask);
        if (superseded) Assert.Empty(notifications.ReceivedCalls());
        else Assert.NotEmpty(notifications.ReceivedCalls());
    }

    private sealed class LoadTestPage : ComponentBase
    {
        [Parameter] public WorkScope Work { get; set; } = default!;
        [Parameter] public string Page { get; set; } = "";
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            var type = Page switch
            {
                "backlog" => typeof(ScrumPilot.FluentWeb.Pages.Backlog),
                "scrum-board" => typeof(ScrumPilot.FluentWeb.Pages.ScrumBoard),
                _ => typeof(DependencyChartPage)
            };
            builder.OpenComponent<CascadingValue<WorkScope>>(0);
            builder.AddAttribute(1, "Value", Work);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent(0, type);
                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    [Fact]
    public async Task Generation_uses_explicit_project_and_discards_cancellation_ignoring_response()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>();
        string? path = null;
        using var client = new HttpClient(new Handler((request, _) =>
        {
            path = request.RequestUri!.PathAndQuery;
            return delayed.Task;
        }))
        { BaseAddress = new Uri("https://localhost/_api/webapi/") };
        using var work = new WorkScope(client, 1, 7);
        using var cut = Render<PbiGeneration>(p => p.Add(x => x.CapturedWork, work));
        cut.Find("input.pbi-generation-input").Change("A problem to solve");
        cut.FindAll("fluent-button").Single(x => x.TextContent.Trim() == "Add").Click();
        var pending = cut.FindAll("fluent-button").Single(x => x.TextContent.Contains("Generate PBIs (")).ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal("/_api/webapi/api/organizations/1/pbi/generateAiPbis?projectId=7", path));
        work.Dispose();
        delayed.SetResult(Json(new[] { new ProductBacklogItem { Title = "Old tenant generation" } }));
        await pending;
        Assert.False(cut.FindComponent<GeneratedPbiDialog>().Instance.Open);
        Assert.DoesNotContain("Old tenant generation", cut.Markup);
    }

    [Fact]
    public async Task Late_editor_save_cannot_emit_a_saved_callback()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>();
        using var client = new HttpClient(new Handler((request, _) => request.Method == HttpMethod.Put
            ? delayed.Task : Task.FromResult(Json(Array.Empty<object>()))))
        { BaseAddress = new Uri("https://localhost/") };
        using var work = new WorkScope(client, 1, 7);
        var callbacks = 0;
        using var cut = Render<PbiDetailsDialog>(p => p
            .Add(x => x.CapturedWork, work)
            .Add(x => x.ProjectId, 7)
            .Add(x => x.Pbi, new ProductBacklogItem { PbiId = 9, ProjectId = 7, Title = "Original" })
            .Add(x => x.Open, true)
            .Add(x => x.OnSaved, _ => callbacks++));
        var pending = cut.Find("form").SubmitAsync();
        work.Dispose();
        delayed.SetResult(Json(new ProductBacklogItem { PbiId = 9, ProjectId = 7, Title = "Late" }));
        await pending;
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task Equal_sprint_ids_in_different_tenants_never_share_a_late_chart()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>();
        using var client = new HttpClient(new Handler((request, _) =>
            request.RequestUri!.AbsolutePath.Contains("/organizations/1/")
                ? delayed.Task : Task.FromResult(Json(new[] { new BugTrendPoint(DateTime.UtcNow, 2, 1) }))))
        { BaseAddress = new Uri("https://localhost/") };
        using var first = new WorkScope(client, 1, 7);
        using var second = new WorkScope(client, 2, 7);
        using var oldChart = Render<BugTrendWidget>(p => p.Add(x => x.CapturedWork, first).Add(x => x.SprintId, 4));
        first.Dispose();
        using var current = Render<BugTrendWidget>(p => p.Add(x => x.CapturedWork, second).Add(x => x.SprintId, 4));
        current.WaitForAssertion(() => Assert.Single(current.FindComponents<ApexChart<BugTrendPoint>>()));
        delayed.SetResult(Json(new[] { new BugTrendPoint(DateTime.UtcNow, 100, 0) }));
        await oldChart.InvokeAsync(() => Task.CompletedTask);
        Assert.Empty(oldChart.FindComponents<ApexChart<BugTrendPoint>>());
        Assert.True(second.IsCurrent);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
