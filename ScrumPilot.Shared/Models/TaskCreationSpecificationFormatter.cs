using System.Text;

namespace ScrumPilot.Shared.Models;

public static class TaskCreationSpecificationFormatter
{
    public static string Format(string title, string description, ProductBacklogItem source, string? guidelines)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Implementation task: {title}");
        text.AppendLine($"\n## Source work item");
        text.AppendLine($"#{source.PbiId} ({source.Type}): {source.Title}");
        text.AppendLine(source.Description);
        text.AppendLine("\n## Task");
        text.AppendLine(description);
        text.AppendLine("\n## Project guidelines");
        text.AppendLine(string.IsNullOrWhiteSpace(guidelines)
            ? "No project guidelines are currently saved. Follow the repository's existing conventions."
            : guidelines);
        text.AppendLine("\n## Instructions for the implementation agent");
        text.AppendLine("- Review the existing system, relevant documentation, and nearby tests before proposing or making changes.");
        text.AppendLine("- Before implementing, ask the user exactly five clarifying questions, one at a time, and wait for their answers so the requirements are understood.");
        text.AppendLine("- Follow the project guidelines and established conventions.");
        text.AppendLine("- Change only the specific files necessary to implement this task; avoid unrelated edits and refactoring.");
        text.AppendLine("- Verify the requested behavior with focused tests and report the exact commands and results.");
        return text.ToString();
    }
}
