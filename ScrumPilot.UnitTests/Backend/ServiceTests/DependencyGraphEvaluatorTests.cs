using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class DependencyGraphEvaluatorTests
{
    [Fact]
    public void MultiplePrerequisites_RequireEveryCompletion_AndKeepIsolatedWorkReady()
    {
        var result = DependencyGraphEvaluator.Evaluate(
            [Item(1, PbiStatus.Done), Item(2), Item(3), Item(4)],
            [new(3, 1), new(3, 2)]);
        Assert.False(result.Nodes[3].DependencyReady);
        Assert.Equal([2], result.Nodes[3].BlockingIds);
        Assert.True(result.Nodes[4].DependencyReady);
    }

    [Fact]
    public void ChildInheritsStoryPrerequisite_ButNotItsOwnParentCompletion()
    {
        var story = Item(1);
        story.Type = PbiType.Story;
        var prerequisite = Item(2, PbiStatus.Done);
        prerequisite.Type = PbiType.Story;
        var child = Item(3);
        child.ParentStoryId = 1;
        var result = DependencyGraphEvaluator.Evaluate([story, prerequisite, child], [new(1, 2)]);
        Assert.True(result.Nodes[3].DependencyReady);
        Assert.False(result.Nodes[1].CompletionSatisfied);
    }

    [Fact]
    public void EffectiveCycle_IncludesInheritedPrerequisitesAndChildRollups()
    {
        var a = Item(1);
        a.Type = PbiType.Story;
        var b = Item(2);
        b.Type = PbiType.Story;
        var a1 = Item(3);
        a1.ParentStoryId = 1;
        var b1 = Item(4);
        b1.ParentStoryId = 2;
        var result = DependencyGraphEvaluator.Evaluate([a, b, a1, b1], [new(2, 1), new(3, 4)]);
        Assert.True(result.HasCycle);
        Assert.DoesNotContain(result.Nodes.Values, node => node.DependencyReady);
    }

    [Fact]
    public void ReopenedChild_InvalidatesDoneParentAndTransitiveDependents()
    {
        var story = Item(1, PbiStatus.Done);
        story.Type = PbiType.Story;
        var child = Item(2);
        child.ParentStoryId = 1;
        var dependent = Item(3, PbiStatus.Done);
        var result = DependencyGraphEvaluator.Evaluate([story, child, dependent, Item(4)],
            [new(3, 1), new(4, 3)]);
        Assert.Equal("Needs review", result.Nodes[1].State);
        Assert.False(result.Nodes[3].CompletionSatisfied);
        Assert.False(result.Nodes[4].DependencyReady);
        Assert.Equal(new HashSet<int> { 1, 2, 3, 4 },
            DependencyGraphEvaluator.DependentClosure(result.CompletionRequirements, [2]));
    }

    [Fact]
    public void ReviewAndMissingPrerequisite_CannotBeTreatedAsSatisfied()
    {
        var result = DependencyGraphEvaluator.Evaluate(
            [Item(1, PbiStatus.Done), Item(2), Item(3)], [new(2, 1), new(3, 999)],
            new HashSet<int> { 1 }, new HashSet<int> { 2, 3 });
        Assert.False(result.Nodes[1].CompletionSatisfied);
        Assert.False(result.Nodes[2].ImplementationEligible);
        Assert.Contains(result.Nodes[3].Reasons, reason => reason.Contains("missing"));
    }

    [Fact]
    public void DenseTwoHundredNodeGraph_IsDeterministic()
    {
        var items = Enumerable.Range(1, 200).Select(id => Item(id, id < 200 ? PbiStatus.Done : PbiStatus.ToDo)).ToArray();
        var links = Enumerable.Range(2, 199)
            .SelectMany(id => Enumerable.Range(Math.Max(1, id - 6), Math.Min(6, id - 1))
                .Select(prerequisite => new DependencyLink(id, prerequisite))).ToArray();
        var result = DependencyGraphEvaluator.Evaluate(items, links);
        Assert.True(links.Length >= 1000);
        Assert.Equal(200, result.Nodes.Count);
        Assert.True(result.Nodes[200].DependencyReady);
        Assert.False(result.HasCycle);
    }

    private static ProductBacklogItem Item(int id, PbiStatus status = PbiStatus.ToDo) =>
        new() { PbiId = id, ProjectId = 1, Title = $"Item {id}", Type = PbiType.Task, Status = status };
}
