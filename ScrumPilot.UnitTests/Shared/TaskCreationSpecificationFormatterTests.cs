using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Shared;

public sealed class TaskCreationSpecificationFormatterTests
{
    [Fact]
    public void FormatIncludesGuidelinesAndRequiredImplementationHandoff()
    {
        var source = new ProductBacklogItem
        {
            PbiId = 42,
            Type = PbiType.Bug,
            Title = "Incorrect totals",
            Description = "Totals are wrong for empty projects."
        };

        var result = TaskCreationSpecificationFormatter.Format(
            "Correct empty project totals",
            "Handle the empty project case.",
            source,
            "Use focused tests and preserve existing response contracts.");

        Assert.Contains("Use focused tests and preserve existing response contracts.", result);
        Assert.Contains("Review the existing system", result);
        Assert.Contains("exactly five clarifying questions", result);
        Assert.Contains("only the specific files necessary", result);
        Assert.Contains("#42 (Bug): Incorrect totals", result);
    }
}
