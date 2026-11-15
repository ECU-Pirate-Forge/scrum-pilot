using ScrumPilot.API.Services;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class TaskFileScopeTests
{
    [Theory]
    [InlineData("../secret")]
    [InlineData("C:\\secret")]
    [InlineData("/etc/passwd")]
    [InlineData("src/**")]
    [InlineData("src//File.cs")]
    [InlineData("src/File.cs ")]
    [InlineData(".git/config")]
    public void AmbiguousPathsAreRejected(string path) =>
        Assert.Throws<ArgumentException>(() => TaskFileScope.Normalize(path));

    [Fact]
    public void VerifyNewFilesAndRejectSymlinkEscapes()
    {
        RepositoryTreeEntry[] tree = [new("src", "tree", "040000", "a"), new("linked", "blob", "120000", "b")];
        var valid = TaskFileScope.Validate([new("src/New.cs", "create", "implementation", "Implements the endpoint.")], tree);
        Assert.Single(valid);
        Assert.Throws<ArgumentException>(() => TaskFileScope.Validate(
            [new("linked/New.cs", "create", "implementation", "Escape")], tree));
        Assert.Throws<ArgumentException>(() => TaskFileScope.Validate(
            [new("SRC/New.cs", "create", "implementation", "Case alias")], tree));
    }

    [Fact]
    public void GitDiffBackslashesCannotAliasApprovedPaths() =>
        Assert.Single(TaskFileScope.Violations(
            [new("src/New.cs", "create", "implementation", "Implementation")],
            [new("src\\New.cs", "added", null)]));

    [Fact]
    public void RenameAndUnapprovedDocumentationAreChecked()
    {
        var violations = TaskFileScope.Violations(
            [new("src/New.cs", "rename", "implementation", "Rename", "src/Old.cs")],
            [new("src/New.cs", "renamed", "src/Other.cs"), new("README.md", "modified", null)]);
        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void ExistingPathMustMatchCaseAndCannotBeCreatedAgain()
    {
        RepositoryTreeEntry[] tree = [new("src/Existing.cs", "blob", "100644", "a")];
        Assert.Throws<ArgumentException>(() => TaskFileScope.Validate(
            [new("src/existing.cs", "modify", "implementation", "Change")], tree));
        Assert.Throws<ArgumentException>(() => TaskFileScope.Validate(
            [new("src/Existing.cs", "create", "implementation", "Change")], tree));
    }
}
