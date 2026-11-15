namespace ScrumPilot.API.Services;

public sealed record FileScopeRule(string Path, string Operation, string Category, string Rationale, string? PreviousPath = null);

public static class TaskFileScope
{
    private static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
        { "create", "modify", "delete", "rename" };
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
        { "implementation", "test", "documentation" };

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path != path.Trim())
            throw new ArgumentException("An exact repository-relative path is required.");
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Any(character => char.IsControl(character)
                || character is ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsSurrogate(character))
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."
                || segment.Equals(".git", StringComparison.OrdinalIgnoreCase) || segment.EndsWith('.') || segment.EndsWith(' ')))
            throw new ArgumentException("Paths cannot be absolute, ambiguous, or contain traversal, control characters, or wildcards.");
        return normalized;
    }

    public static IReadOnlyList<FileScopeRule> Validate(
        IEnumerable<FileScopeRule> rules, IReadOnlyList<RepositoryTreeEntry> tree)
    {
        var result = new List<FileScopeRule>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = tree.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            var path = Normalize(rule.Path);
            if (!used.Add(path)) throw new ArgumentException($"Duplicate or case-ambiguous scope path: {path}.");
            if (!Operations.Contains(rule.Operation) || !Categories.Contains(rule.Category)
                || string.IsNullOrWhiteSpace(rule.Rationale))
                throw new ArgumentException("Every file requires an operation, category, and rationale.");
            var caseAlias = tree.Any(entry => entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && entry.Path != path);
            if (caseAlias) throw new ArgumentException($"The path's case must match the repository: {path}.");
            var exists = entries.TryGetValue(path, out var entry);
            if (rule.Operation is "create" or "rename")
            {
                if (exists) throw new ArgumentException($"The new path already exists: {path}.");
            }
            else if (!exists || entry!.Type != "blob" || entry.Mode is not ("100644" or "100755"))
                throw new ArgumentException($"The existing path is not a verified regular file: {path}.");
            CheckAncestors(path, tree);
            string? previous = null;
            if (rule.Operation == "rename")
            {
                previous = Normalize(rule.PreviousPath ?? "");
                if (!used.Add(previous) || !entries.TryGetValue(previous, out var source)
                    || source.Type != "blob" || source.Mode is not ("100644" or "100755"))
                    throw new ArgumentException($"The rename source is not an unambiguous regular file: {previous}.");
                CheckAncestors(previous, tree);
            }
            else if (rule.PreviousPath is not null)
                throw new ArgumentException("Only a rename may specify a previous path.");
            result.Add(rule with { Path = path, PreviousPath = previous });
        }
        if (result.Count == 0) throw new ArgumentException("At least one explicit implementation, test, or documentation file is required.");
        return result;
    }

    public static IReadOnlyList<string> Violations(IEnumerable<FileScopeRule> allowed, IEnumerable<PullRequestFile> changed)
    {
        var rules = allowed.ToDictionary(rule => Normalize(rule.Path), StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var file in changed)
        {
            string path;
            try { path = Normalize(file.Path); }
            catch (ArgumentException) { violations.Add($"Unsupported repository path: {file.Path}"); continue; }
            if (path != file.Path)
            {
                violations.Add($"Noncanonical repository path: {file.Path}");
                continue;
            }
            var operation = file.Status switch
            {
                "added" => "create",
                "modified" => "modify",
                "removed" => "delete",
                "renamed" => "rename",
                _ => "unsupported"
            };
            if (!rules.TryGetValue(path, out var rule) || rule.Operation != operation
                || operation == "rename" && rule.PreviousPath != file.PreviousPath)
                violations.Add($"{operation}: {path}" + (file.PreviousPath is null ? "" : $" (from {file.PreviousPath})"));
        }
        return violations;
    }

    public static bool IsSafeContextPath(string path)
    {
        var segments = Normalize(path).Split('/');
        if (segments.Any(segment => segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("vendor", StringComparison.OrdinalIgnoreCase)))
            return false;
        var file = segments[^1];
        return !file.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            && !file.Contains("secret", StringComparison.OrdinalIgnoreCase)
            && !file.Contains("credential", StringComparison.OrdinalIgnoreCase)
            && !file.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
            && !file.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
            && !file.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckAncestors(string path, IReadOnlyList<RepositoryTreeEntry> tree)
    {
        foreach (var entry in tree)
            if (path.StartsWith(entry.Path + "/", StringComparison.OrdinalIgnoreCase))
            {
                if (!path.StartsWith(entry.Path + "/", StringComparison.Ordinal))
                    throw new ArgumentException($"Directory case must match the repository: {path}.");
                if (entry.Type != "tree" || entry.Mode != "040000")
                    throw new ArgumentException($"The path crosses a symlink, submodule, or regular file: {path}.");
            }
    }
}
