using Microsoft.AspNetCore.Components;

namespace ScrumPilot.Web.Services;

/// <summary>All work components require an explicit, immutable route boundary.</summary>
public abstract class WorkComponentBase : ComponentBase, IHandleEvent
{
    [Parameter] public int OrganizationId { get; set; }
    private WorkScope? _capturedScope;
    [CascadingParameter] public WorkScope? Work { get; set; }
    // Dialogs rendered by a provider receive the captured scope explicitly.
    [Parameter] public WorkScope? CapturedWork { get; set; }
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
        catch (OperationCanceledException) when (!Scope.IsCurrent)
        {
            // Navigation disposed this editor. Cancellation is not a user-visible error.
        }
    }
}
