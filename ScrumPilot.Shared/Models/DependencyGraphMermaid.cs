using System.Text;

namespace ScrumPilot.Shared.Models;

public static class DependencyGraphMermaid
{
    public static string Write(IReadOnlyCollection<DependencyGraphNodeDto> nodes,
        IEnumerable<DependencyGraphEdgeDto> edges, long revision)
    {
        var text = new StringBuilder($"%% ScrumPilot graph revision {revision}\nflowchart LR\n");
        if (nodes.Count == 0) return text.AppendLine("EMPTY[\"No matching work items\"]").ToString();
        text.AppendLine("classDef ready fill:#dff6dd,stroke:#107c10,color:#0b4a0b");
        text.AppendLine("classDef blocked fill:#fde7e9,stroke:#a4262c,color:#601318");
        text.AppendLine("classDef active fill:#fff4ce,stroke:#8a6d00,color:#3b3000");
        text.AppendLine("classDef done fill:#eeeeee,stroke:#666666,color:#333333");
        text.AppendLine("classDef review fill:#f3e5ff,stroke:#744da9,color:#402466");
        var visible = nodes.Select(node => node.Item.PbiId).ToHashSet();
        foreach (var node in nodes.OrderBy(node => node.Item.PbiId))
        {
            var item = node.Item;
            text.AppendLine($"PBI{item.PbiId}[\"{(node.IsBoundary ? "External / " : "")}#{item.PbiId} {Escape(item.Title)} - {node.State}\"]");
            var style = node.State switch
            { "Ready" => "ready", "Done" => "done", "Active" => "active", "Needs review" => "review", _ => "blocked" };
            text.AppendLine($"class PBI{item.PbiId} {style}");
        }
        foreach (var problem in nodes
                     .Where(node => node.Item.ProblemStatementId.HasValue
                         && node.Item.Type is PbiType.Story or PbiType.Bug)
                     .GroupBy(node => node.Item.ProblemStatementId!.Value)
                     .OrderBy(group => group.Key))
        {
            text.AppendLine($"subgraph PROBLEM{problem.Key}[\"Problem #{problem.Key}\"]");
            foreach (var node in problem.OrderBy(node => node.Item.PbiId))
                text.AppendLine($"PBI{node.Item.PbiId}");
            text.AppendLine("end");
        }
        foreach (var group in nodes.Where(node => node.Item.ParentStoryId.HasValue)
                     .GroupBy(node => node.Item.ParentStoryId!.Value).OrderBy(group => group.Key))
        {
            text.AppendLine($"subgraph STORY{group.Key}[\"Tasks for story #{group.Key}\"]");
            foreach (var node in group.OrderBy(node => node.Item.PbiId)) text.AppendLine($"PBI{node.Item.PbiId}");
            text.AppendLine("end");
        }
        foreach (var edge in edges.Distinct().OrderBy(edge => edge.PrerequisiteId).ThenBy(edge => edge.DependentId))
        {
            if (!visible.Contains(edge.DependentId)) continue;
            if (!visible.Contains(edge.PrerequisiteId))
                text.AppendLine($"PBI{edge.PrerequisiteId}[\"Unresolved prerequisite #{edge.PrerequisiteId}\"]:::blocked");
            text.AppendLine($"PBI{edge.PrerequisiteId} --> PBI{edge.DependentId}");
        }
        return text.ToString();
    }

    public static string Escape(string value)
    {
        var safe = new StringBuilder();
        foreach (var character in value.Take(100))
        {
            if (char.IsAsciiLetterOrDigit(character) || character is ' ' or '-' or '_' or '.' or ',')
                safe.Append(character);
            else if (!char.IsControl(character))
                safe.Append('#').Append((int)character).Append(';');
        }
        return safe.ToString();
    }
}
