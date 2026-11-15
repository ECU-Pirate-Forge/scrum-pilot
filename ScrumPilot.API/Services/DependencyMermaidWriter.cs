using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public static class DependencyMermaidWriter
{
    public static string Write(IReadOnlyCollection<ProductBacklogItem> visibleItems,
        IEnumerable<DependencyLink> links, DependencyEvaluation evaluation, int? sprintId, long revision)
    {
        return DependencyGraphMermaid.Write(visibleItems.Select(item =>
        {
            var state = evaluation.Nodes[item.PbiId];
            return new DependencyGraphNodeDto(item, state.State, state.CompletionSatisfied, state.DependencyReady,
                state.ImplementationEligible, sprintId.HasValue && item.SprintId != sprintId,
                state.BlockingIds.ToArray(), state.Reasons.ToArray());
        }).ToArray(), links.Select(link => new DependencyGraphEdgeDto(link.DependentId, link.PrerequisiteId)), revision);
    }

    public static string Escape(string value)
    {
        return DependencyGraphMermaid.Escape(value);
    }
}
