namespace ScrumPilot.FluentWeb.Components.ProjectManagement;

public sealed class ProjectEditorModel
{
    public ProjectEditorModel() { }
    public ProjectEditorModel(int projectId, string projectName, string? description = null)
    {
        ProjectId = projectId;
        ProjectName = projectName;
        Description = description;
    }

    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed record ProjectPbiFilter(int? SprintId = null, int? EpicId = null);
