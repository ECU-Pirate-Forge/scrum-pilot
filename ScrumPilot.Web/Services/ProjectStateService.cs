using ScrumPilot.Shared.Models;

namespace ScrumPilot.Web.Services;

public class ProjectStateService
{
    public Project? SelectedProject { get; private set; }
    public int? SelectedProjectId => SelectedProject?.ProjectId;

    public event Action? OnChange;
    public event Action? OnProjectListChanged;

    public void SetProject(Project? project)
    {
        SelectedProject = project;
        OnChange?.Invoke();
    }

    public void NotifyProjectListChanged() => OnProjectListChanged?.Invoke();
}
