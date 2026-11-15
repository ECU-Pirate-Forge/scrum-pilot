using Microsoft.AspNetCore.Components;

namespace ScrumPilot.FluentWeb.Services;

/// <summary>Framework-specific lifetime adapter; transport remains UI-neutral.</summary>
public abstract class WorkComponentBase : ComponentBase, IHandleEvent
{
    [Parameter] public int OrganizationId { get; set; }
    [CascadingParameter] public WorkScope? Work { get; set; }
    [Parameter] public WorkScope? CapturedWork { get; set; }
    private WorkScope? _capturedScope;
    protected WorkScope Scope => _capturedScope ??= CapturedWork ?? Work
        ?? throw new InvalidOperationException("Work requires an organization route boundary.");
    protected WorkHttpClient Http => Scope.Http;
    private MetricsDashboardService? _metrics;
    protected MetricsDashboardService MetricsSvc => _metrics ??= new MetricsDashboardService(Http);

    async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg)
    {
        if (!Scope.IsCurrent) return;
        try
        {
            var task = callback.InvokeAsync(arg);
            StateHasChanged();
            await task;
            if (Scope.IsCurrent) StateHasChanged();
        }
        catch (OperationCanceledException) when (!Scope.IsCurrent) { }
    }
}
