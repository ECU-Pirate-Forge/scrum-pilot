using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed record DependencyLink(int DependentId, int PrerequisiteId);

public sealed record DependencyNodeState(
    int PbiId,
    string State,
    bool CompletionSatisfied,
    bool DependencyReady,
    bool ImplementationEligible,
    IReadOnlyList<int> BlockingIds,
    IReadOnlyList<string> Reasons);

public sealed record DependencyEvaluation(
    IReadOnlyDictionary<int, DependencyNodeState> Nodes,
    IReadOnlyDictionary<int, HashSet<int>> CompletionRequirements,
    bool HasCycle);

/// <summary>Evaluates execution constraints, including inherited prerequisites and story rollups.</summary>
public static class DependencyGraphEvaluator
{
    public static DependencyEvaluation Evaluate(
        IReadOnlyCollection<ProductBacklogItem> items,
        IEnumerable<DependencyLink> links,
        IReadOnlySet<int>? needsReview = null,
        IReadOnlySet<int>? approvedSpecifications = null)
    {
        var byId = items.ToDictionary(item => item.PbiId);
        var direct = items.ToDictionary(item => item.PbiId, _ => new HashSet<int>());
        foreach (var link in links)
        {
            if (direct.TryGetValue(link.DependentId, out var prerequisites))
                prerequisites.Add(link.PrerequisiteId);
        }

        var execution = direct.ToDictionary(pair => pair.Key, pair => new HashSet<int>(pair.Value));
        var invalidParents = new HashSet<int>();
        foreach (var item in items.Where(item => item.ParentStoryId.HasValue))
        {
            if (!byId.TryGetValue(item.ParentStoryId!.Value, out var parent)
                || parent.Type != PbiType.Story || item.Type != PbiType.Task
                || parent.ProjectId != item.ProjectId)
            {
                invalidParents.Add(item.PbiId);
                continue;
            }

            execution[item.PbiId].UnionWith(direct[parent.PbiId]);
        }

        var completion = execution.ToDictionary(pair => pair.Key, pair => new HashSet<int>(pair.Value));
        foreach (var child in items.Where(item => item.ParentStoryId.HasValue && !item.IsDraft))
        {
            if (completion.TryGetValue(child.ParentStoryId!.Value, out var requirements))
                requirements.Add(child.PbiId);
        }

        // The effective graph also contains child rollups: a stored-edge DAG alone
        // would miss a deadlock between a story prerequisite and a cross-story task.
        var colors = new Dictionary<int, int>();
        var satisfied = new Dictionary<int, bool>();
        var path = new List<int>();
        var cyclicNodes = new HashSet<int>();
        var hasCycle = false;
        bool IsSatisfied(int id)
        {
            if (!byId.TryGetValue(id, out var item)) return false;
            if (satisfied.TryGetValue(id, out var cached)) return cached;
            if (colors.GetValueOrDefault(id) == 1)
            {
                hasCycle = true;
                cyclicNodes.UnionWith(path.Skip(path.IndexOf(id)));
                return false;
            }

            colors[id] = 1;
            path.Add(id);
            var prerequisitesSatisfied = true;
            foreach (var prerequisite in completion[id])
                prerequisitesSatisfied = IsSatisfied(prerequisite) && prerequisitesSatisfied;
            colors[id] = 2;
            path.RemoveAt(path.Count - 1);
            var result = !item.IsDraft && item.Status == PbiStatus.Done
                && !invalidParents.Contains(id) && needsReview?.Contains(id) != true
                && prerequisitesSatisfied;
            satisfied[id] = result;
            return result;
        }

        foreach (var id in byId.Keys) IsSatisfied(id);

        var nodes = new Dictionary<int, DependencyNodeState>();
        foreach (var item in items)
        {
            var blockers = execution[item.PbiId].Where(id => !satisfied.GetValueOrDefault(id)).Order().ToArray();
            var reasons = new List<string>();
            if (invalidParents.Contains(item.PbiId)) reasons.Add("Parent story is missing or invalid.");
            if (needsReview?.Contains(item.PbiId) == true) reasons.Add("A prerequisite changed; acceptance review is required.");
            foreach (var id in blockers)
                reasons.Add(byId.ContainsKey(id) ? $"Prerequisite #{id} is not satisfied." : $"Prerequisite #{id} is missing.");
            if (cyclicNodes.Contains(item.PbiId)) reasons.Add("This work belongs to an effective dependency cycle; repair dependencies before starting work.");
            if (item.IsDraft) reasons.Add("Draft work is not committed.");
            if (item.Status == PbiStatus.Done && !satisfied[item.PbiId] && blockers.Length == 0
                && completion[item.PbiId].Any(id => !satisfied.GetValueOrDefault(id)))
                reasons.Add("Child work is incomplete or requires review.");

            var ready = item.Status == PbiStatus.ToDo && reasons.Count == 0;
            var state = item.IsDraft ? "Draft"
                : needsReview?.Contains(item.PbiId) == true || item.Status == PbiStatus.Done && !satisfied[item.PbiId] ? "Needs review"
                : item.Status == PbiStatus.Done ? "Done"
                : item.Status is PbiStatus.InProgress or PbiStatus.InReview ? "Active"
                : ready ? "Ready" : "Blocked";
            nodes[item.PbiId] = new(item.PbiId, state, satisfied[item.PbiId], ready,
                ready && approvedSpecifications?.Contains(item.PbiId) == true, blockers, reasons);
        }
        return new(nodes, completion, hasCycle);
    }

    public static HashSet<int> DependentClosure(
        IReadOnlyDictionary<int, HashSet<int>> completionRequirements,
        IEnumerable<int> changedIds)
    {
        var changed = changedIds.ToHashSet();
        var pending = new Queue<int>(changed);
        while (pending.TryDequeue(out var prerequisite))
        {
            foreach (var pair in completionRequirements)
            {
                if (pair.Value.Contains(prerequisite) && changed.Add(pair.Key))
                    pending.Enqueue(pair.Key);
            }
        }
        return changed;
    }
}
