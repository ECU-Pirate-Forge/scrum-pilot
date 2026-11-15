using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class RepositoryContextService(OrganizationContext db, GitHubAppClient github, GitHubRepositoryRegistry registry)
{
    public async Task<RepositorySnapshot> SnapshotAsync(int projectId, string branch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(branch)) throw new ArgumentException("Select a repository branch.");
        var repository = await db.ProjectRepositories.AsNoTracking().SingleOrDefaultAsync(repository => repository.ProjectId == projectId, ct)
            ?? throw new InvalidOperationException("An Owner must link a GitHub repository before creating an implementation-ready specification.");
        await registry.RequireCurrentAsync(repository, ct);
        var tree = await github.GetTreeAsync(repository.InstallationId, repository.GitHubRepositoryId,
            repository.Owner, repository.Name, branch, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        await registry.RequireCurrentAsync(repository, ct);
        var existing = await db.RepositorySnapshots.SingleOrDefaultAsync(snapshot =>
            snapshot.ProjectRepositoryId == repository.ProjectRepositoryId && snapshot.CommitSha == tree.CommitSha && snapshot.Branch == branch, ct);
        if (existing is not null)
        {
            await transaction.CommitAsync(ct);
            return existing;
        }
        var snapshot = new RepositorySnapshot
        {
            ProjectId = projectId,
            ProjectRepositoryId = repository.ProjectRepositoryId,
            CommitSha = tree.CommitSha,
            TreeSha = tree.TreeSha,
            TreeJson = JsonSerializer.Serialize(tree),
            Branch = branch
        };
        db.RepositorySnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return snapshot;
    }

    public static VerifiedRepositoryTree ReadTree(RepositorySnapshot snapshot) =>
        JsonSerializer.Deserialize<VerifiedRepositoryTree>(snapshot.TreeJson)
        ?? throw new InvalidOperationException("Repository tree metadata is invalid. Create a verified snapshot.");

    public async Task<IReadOnlyDictionary<string, string>> SourceAsync(int projectId, int snapshotId,
        IEnumerable<string> selectedPaths, CancellationToken ct)
    {
        var snapshot = await db.RepositorySnapshots.SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.RepositorySnapshotId == snapshotId, ct) ?? throw new KeyNotFoundException();
        var repository = await db.ProjectRepositories.AsNoTracking().SingleAsync(item =>
            item.ProjectId == projectId && item.ProjectRepositoryId == snapshot.ProjectRepositoryId, ct);
        await registry.RequireCurrentAsync(repository, ct);
        var paths = selectedPaths.Distinct(StringComparer.Ordinal).ToArray();
        if (paths.Length > 12) throw new ArgumentException("Select at most 12 relevant source files for one generation request.");
        var tree = ReadTree(snapshot);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawPath in paths)
        {
            var path = TaskFileScope.Normalize(rawPath);
            if (!TaskFileScope.IsSafeContextPath(path)) throw new ArgumentException($"This path is excluded from automatic source context: {path}.");
            var entry = tree.Entries.SingleOrDefault(item => item.Path == path && item.Type == "blob"
                && item.Mode is "100644" or "100755") ?? throw new ArgumentException($"Source file is not a verified regular file: {path}.");
            result[path] = await github.GetTextAsync(repository.InstallationId, repository.GitHubRepositoryId,
                repository.Owner, repository.Name, entry.Sha, ct);
        }
        await registry.RequireCurrentAsync(repository, ct);
        return result;
    }
}
