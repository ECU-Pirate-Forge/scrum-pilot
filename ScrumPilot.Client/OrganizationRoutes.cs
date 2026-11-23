namespace ScrumPilot.Client;

/// <summary>Builds explicit tenant URLs. Local IDs are meaningful only inside an organization.</summary>
public static class OrganizationRoutes
{
    private static readonly HashSet<string> WorkPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "home","backlog", "scrum-board", "project-management", "draft-stories", "pbigeneration",
        "metrics", "dependency-chart", "planning-poker", "organization-management", "task-planning", "pbi-generation"
    };

    public static string Page(int organizationId, string page, int? projectId = null)
    {
        Validate(organizationId);
        // Relative to the site's <base>, including Aspire's /web/ or /fluentweb/ mount.
        var path = $"organizations/{organizationId}/{page.Trim('/')}";
        return projectId is > 0 ? $"{path}?projectId={projectId.Value}" : path;
    }

    public static string Api(int organizationId, string suffix)
    {
        Validate(organizationId);
        suffix = suffix.TrimStart('/');
        if (suffix.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            suffix = suffix[4..];
        if (suffix.StartsWith("organizations/", StringComparison.OrdinalIgnoreCase)
            || suffix.Contains("://", StringComparison.Ordinal)
            || suffix.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Supply an unqualified work API suffix.", nameof(suffix));
        return $"api/organizations/{organizationId}/{suffix}";
    }

    public static string PokerHub(int organizationId)
    {
        Validate(organizationId);
        return $"hubs/organizations/{organizationId}/planning-poker";
    }

    public static string ProjectWorkHub(int organizationId)
    {
        Validate(organizationId);
        return $"hubs/organizations/{organizationId}/project-work";
    }

    public static int? OrganizationId(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        var index = Array.FindIndex(segments, segment => segment.Equals("organizations", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && segments.Length > index + 1
            && int.TryParse(segments[index + 1], out var id) && id > 0 ? id : null;
    }

    public static int? ProjectId(Uri uri)
    {
        var values = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => Uri.UnescapeDataString(pair[0]).Equals("projectId", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return values.Count == 1 && values[0].Length == 2
            && int.TryParse(Uri.UnescapeDataString(values[0][1]), out var id) && id > 0 ? id : null;
    }

    public static string CurrentPage(Uri uri) => uri.AbsolutePath.Trim('/').Split('/').LastOrDefault() ?? "";
    public static bool IsWorkPage(Uri uri) => WorkPages.Contains(CurrentPage(uri));
    public static bool IsLegacyWorkLink(Uri uri) =>
        OrganizationId(uri) is null && IsWorkPage(uri)
        && !CurrentPage(uri).Equals("organization-management", StringComparison.OrdinalIgnoreCase);

    private static void Validate(int organizationId)
    {
        if (organizationId <= 0) throw new ArgumentOutOfRangeException(nameof(organizationId));
    }
}
