namespace ScrumPilot.Shared.Models;

public sealed record DependencyGraphNodeDto(ProductBacklogItem Item, string State,
    bool CompletionSatisfied, bool DependencyReady, bool ImplementationEligible,
    bool IsBoundary, int[] BlockingIds, string[] Reasons);
public sealed record DependencyGraphEdgeDto(int DependentId, int PrerequisiteId);
public sealed record DependencyGraphDto(int ProjectId, int? SprintId, long Revision,
    DependencyGraphNodeDto[] Nodes, DependencyGraphEdgeDto[] Edges, string Mermaid, string[] Diagnostics);
public sealed record SetDependenciesRequest(int DependentId, int[] PrerequisiteIds, long ExpectedRevision);
public sealed record ReviewDependencyRequest(int PbiId, long InvalidationRevision);
