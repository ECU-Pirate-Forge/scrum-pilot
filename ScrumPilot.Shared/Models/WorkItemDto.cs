namespace Scrumpilot.Shared;

public class WorkItemDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = "ToDo";

    public string TaskType { get; set; } = "Story";

    public string Priority { get; set; } = "Medium";

    public string Assignee { get; set; } = "??";
}