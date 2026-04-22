using ScrumPilot.Shared.Models;

namespace ScrumPilot.Web.Services;

/// <summary>
/// Singleton service that tracks the currently selected project across all pages
/// and raises events so subscribed components can react to project changes.
/// </summary>
public class ProjectStateService
{
    /// <summary>The project currently selected by the user, or <c>null</c> if none is selected.</summary>
    public Project? SelectedProject { get; private set; }

    /// <summary>Convenience accessor for <see cref="SelectedProject"/>'s primary key.</summary>
    public int? SelectedProjectId => SelectedProject?.ProjectId;

    /// <summary>Raised when the selected project changes.</summary>
    public event Action? OnChange;

    /// <summary>Raised when the list of available projects has been modified (e.g., after a create or delete).</summary>
    public event Action? OnProjectListChanged;

    /// <summary>Sets the active project and notifies all subscribers via <see cref="OnChange"/>.</summary>
    public void SetProject(Project? project)
    {
        SelectedProject = project;
        OnChange?.Invoke();
    }

    /// <summary>Triggers <see cref="OnProjectListChanged"/> to signal that the project list should be refreshed.</summary>
    public void NotifyProjectListChanged() => OnProjectListChanged?.Invoke();
}
