using Microsoft.AspNetCore.Components;

namespace ScrumPilot.FluentWeb.Services;

/// <summary>A keyed planning editor also ends its work lifetime when removed from the page.</summary>
public abstract class PlanningComponentBase : ComponentBase, IDisposable, IHandleEvent
{
    [Parameter] public int OrganizationId { get; set; }
    [CascadingParameter] public WorkScope? Work { get; set; }
    [Parameter] public WorkScope? CapturedWork { get; set; }
    private WorkScope? _scope;
    private object? _identity;
    protected virtual object PlanningIdentity => GetType();
    protected WorkScope Scope => _scope ??= CreateScope();
    protected WorkHttpClient Http => Scope.Http;

    private WorkScope CreateScope()
    {
        var parent = CapturedWork ?? Work
            ?? throw new InvalidOperationException("Planning requires an organization route boundary.");
        _identity = PlanningIdentity;
        return parent.CreateChild();
    }

    protected override void OnParametersSet()
    {
        // Editors are keyed by artifact. Never retarget an outstanding operation to a new artifact.
        if (_scope is not null && !Equals(_identity, PlanningIdentity)) _scope.Dispose();
    }

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

    public virtual void Dispose() => _scope?.Dispose();
}
