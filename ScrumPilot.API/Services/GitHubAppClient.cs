using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ScrumPilot.API.Configuration;

namespace ScrumPilot.API.Services;

public sealed record RepositoryTreeEntry(string Path, string Type, string Mode, string Sha);
public sealed record VerifiedRepositoryTree(long RepositoryId, string Branch, string CommitSha, string TreeSha,
    IReadOnlyList<RepositoryTreeEntry> Entries);
public sealed record PullRequestFile(string Path, string Status, string? PreviousPath);
public sealed record VerifiedPullRequest(string HeadSha, string BaseSha, string MergeBaseSha,
    IReadOnlyList<PullRequestFile> Files);
public sealed record PullRequestIdentity(string HeadSha, bool IsOpen);

/// <summary>Fixed-host GitHub App access. This client never checks out or executes repository code.</summary>
public sealed class GitHubAppClient(HttpClient http, IOptions<GitHubAppOptions> options)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.AppId)
        && !string.IsNullOrWhiteSpace(options.Value.PrivateKeyPem);
    public bool IsWebhookConfigured => !string.IsNullOrWhiteSpace(options.Value.WebhookSecret);

    public async Task<VerifiedRepositoryTree> GetTreeAsync(
        long installationId, long repositoryId, string owner, string repository, string branch,
        CancellationToken cancellationToken)
    {
        var root = RepositoryPath(owner, repository);
        var token = await InstallationToken(installationId, repositoryId, cancellationToken);
        using var repo = await Get(root, token, cancellationToken);
        if (repo.RootElement.GetProperty("id").GetInt64() != repositoryId)
            throw new InvalidOperationException("The configured repository identity changed. An Owner must review the configuration.");
        using var commit = await Get($"{root}/commits/{Uri.EscapeDataString(branch)}", token, cancellationToken);
        var sha = commit.RootElement.GetProperty("sha").GetString()!;
        var treeSha = commit.RootElement.GetProperty("commit").GetProperty("tree").GetProperty("sha").GetString()!;
        using var tree = await Get($"{root}/git/trees/{treeSha}?recursive=1", token, cancellationToken);
        if (tree.RootElement.GetProperty("truncated").GetBoolean())
            throw new InvalidOperationException("GitHub returned a truncated repository tree. Narrow the repository context before approving a specification.");
        var entries = tree.RootElement.GetProperty("tree").EnumerateArray().Select(entry =>
            new RepositoryTreeEntry(entry.GetProperty("path").GetString()!, entry.GetProperty("type").GetString()!,
                entry.GetProperty("mode").GetString()!, entry.GetProperty("sha").GetString()!)).ToArray();
        return new(repositoryId, branch, sha, treeSha, entries);
    }

    public async Task<string> GetTextAsync(
        long installationId, long repositoryId, string owner, string repository, string blobSha,
        CancellationToken cancellationToken)
    {
        if (blobSha.Length != 40 || !blobSha.All(Uri.IsHexDigit))
            throw new ArgumentException("A verified blob SHA is required.");
        var token = await InstallationToken(installationId, repositoryId, cancellationToken);
        using var blob = await Get($"{RepositoryPath(owner, repository)}/git/blobs/{blobSha}", token, cancellationToken);
        var body = blob.RootElement;
        if (body.GetProperty("size").GetInt64() > 128 * 1024 || body.GetProperty("encoding").GetString() != "base64")
            throw new InvalidOperationException("The selected source file exceeds the text context limit.");
        var bytes = Convert.FromBase64String(body.GetProperty("content").GetString()!);
        if (bytes.Contains((byte)0)) throw new InvalidOperationException("Binary source content cannot be used as AI context.");
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    public async Task<VerifiedPullRequest> GetPullRequestAsync(
        long installationId, long repositoryId, string owner, string repository, int number,
        CancellationToken cancellationToken)
    {
        if (number <= 0) throw new ArgumentException("A positive pull request number is required.");
        var root = RepositoryPath(owner, repository);
        var token = await InstallationToken(installationId, repositoryId, cancellationToken);
        using var repo = await Get(root, token, cancellationToken);
        if (repo.RootElement.GetProperty("id").GetInt64() != repositoryId)
            throw new InvalidOperationException("The pull request repository does not match the configured repository.");
        using var pr = await Get($"{root}/pulls/{number}", token, cancellationToken);
        var expectedCount = pr.RootElement.GetProperty("changed_files").GetInt32();
        if (expectedCount >= 3000)
            throw new InvalidOperationException("The complete PR diff cannot be verified within GitHub's file API limit.");
        var head = pr.RootElement.GetProperty("head").GetProperty("sha").GetString()!;
        var baseSha = pr.RootElement.GetProperty("base").GetProperty("sha").GetString()!;
        using var comparison = await Get($"{root}/compare/{baseSha}...{head}", token, cancellationToken);
        var mergeBase = comparison.RootElement.GetProperty("merge_base_commit").GetProperty("sha").GetString()!;
        var files = new List<PullRequestFile>();
        for (var page = 1; page <= 30; page++)
        {
            using var batch = await Get($"{root}/pulls/{number}/files?per_page=100&page={page}", token, cancellationToken);
            foreach (var file in batch.RootElement.EnumerateArray())
                files.Add(new(file.GetProperty("filename").GetString()!, file.GetProperty("status").GetString()!,
                    file.TryGetProperty("previous_filename", out var previous) ? previous.GetString() : null));
            if (batch.RootElement.GetArrayLength() < 100) break;
        }

        if (files.Count != expectedCount || files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != files.Count)
            throw new InvalidOperationException("GitHub did not return a complete, unique PR file list. Scope remains unverified.");
        using var current = await Get($"{root}/pulls/{number}", token, cancellationToken);
        if (current.RootElement.GetProperty("head").GetProperty("sha").GetString() != head
            || current.RootElement.GetProperty("base").GetProperty("sha").GetString() != baseSha)
            throw new InvalidOperationException("The PR changed during validation. Retry against its current head and base.");
        var before = await FileModes(root, mergeBase, token, cancellationToken);
        var after = await FileModes(root, head, token, cancellationToken);
        files = files.Select(file =>
        {
            before.TryGetValue(file.PreviousPath ?? file.Path, out var oldMode);
            after.TryGetValue(file.Path, out var newMode);
            var regularNewFile = newMode is "100644" or "100755";
            var unsafeChange = file.Status == "added" && !regularNewFile
                || file.Status is "modified" or "renamed" && (newMode != oldMode || !regularNewFile);
            return unsafeChange ? file with { Status = "type-or-mode-changed" } : file;
        }).ToList();
        return new(head, baseSha, mergeBase, files);
    }

    public async Task<PullRequestIdentity> GetPullRequestIdentityAsync(long installationId, long repositoryId,
        string owner, string repository, int number, CancellationToken ct)
    {
        if (number <= 0) throw new ArgumentException("A positive pull request number is required.");
        var root = RepositoryPath(owner, repository);
        var token = await InstallationToken(installationId, repositoryId, ct);
        using var repo = await Get(root, token, ct);
        if (repo.RootElement.GetProperty("id").GetInt64() != repositoryId)
            throw new InvalidOperationException("The PR repository identity changed.");
        using var pr = await Get($"{root}/pulls/{number}", token, ct);
        return new(pr.RootElement.GetProperty("head").GetProperty("sha").GetString()!,
            pr.RootElement.GetProperty("state").GetString() == "open");
    }

    public async Task EnsureExclusivePullRequestHeadAsync(long installationId, long repositoryId, string owner,
        string repository, int number, string headSha, CancellationToken ct)
    {
        var token = await InstallationToken(installationId, repositoryId, ct);
        var seen = new HashSet<int>();
        var matching = new List<int>();
        for (var page = 1; page <= 100; page++)
        {
            using var batch = await Get($"{RepositoryPath(owner, repository)}/pulls?state=open&per_page=100&page={page}", token, ct);
            foreach (var pr in batch.RootElement.EnumerateArray())
            {
                var id = pr.GetProperty("number").GetInt32();
                if (!seen.Add(id)) throw new InvalidOperationException("GitHub's open PR enumeration changed. Retry verification.");
                if (pr.GetProperty("head").GetProperty("sha").GetString() == headSha) matching.Add(id);
            }
            if (matching.Count > 1)
                throw new InvalidOperationException("Required checks are commit-scoped. Open PRs must use distinct head commits; shared heads cannot receive scope approval.");
            if (batch.RootElement.GetArrayLength() < 100)
            {
                if (matching.Count != 1 || matching[0] != number)
                    throw new InvalidOperationException("The PR is no longer the unique open PR for this commit. Refresh and retry.");
                return;
            }
        }
        throw new InvalidOperationException("The complete set of open PR heads could not be verified.");
    }

    public async Task<long> PublishCheckAsync(long installationId, long repositoryId, string owner, string repository,
        string headSha, bool success, string summary, CancellationToken cancellationToken)
    {
        var token = await InstallationToken(installationId, repositoryId, cancellationToken);
        using var request = Request(HttpMethod.Post, $"{RepositoryPath(owner, repository)}/check-runs", token);
        request.Content = JsonContent.Create(new
        {
            name = "ScrumPilot / task-scope",
            head_sha = headSha,
            status = "completed",
            conclusion = success ? "success" : "failure",
            output = new { title = success ? "Approved task file scope" : "Scope validation failed", summary }
        });
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return result.RootElement.GetProperty("id").GetInt64();
    }

    public bool VerifyWebhook(ReadOnlySpan<byte> body, string signature)
    {
        if (string.IsNullOrWhiteSpace(options.Value.WebhookSecret)
            || !signature.StartsWith("sha256=", StringComparison.Ordinal) || signature.Length != 71)
            return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature[7..]); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.WebhookSecret), body);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private async Task<string> InstallationToken(long installationId, long repositoryId, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("GitHub App is not configured. Configure its App ID and private key on the API.");
        if (installationId <= 0 || repositoryId <= 0) throw new ArgumentException("GitHub installation and repository IDs must be positive.");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(options.Value.PrivateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal));
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(issuer: options.Value.AppId, notBefore: now.AddSeconds(-30),
            expires: now.AddMinutes(5), signingCredentials: new(new RsaSecurityKey(rsa)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            }, SecurityAlgorithms.RsaSha256));
        using var request = Request(HttpMethod.Post, $"app/installations/{installationId}/access_tokens",
            new JwtSecurityTokenHandler().WriteToken(jwt));
        request.Content = JsonContent.Create(new
        {
            repository_ids = new[] { repositoryId },
            permissions = new { contents = "read", pull_requests = "read", checks = "write" }
        });
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return result.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("GitHub did not return an installation token.");
    }

    private async Task<JsonDocument> Get(string path, string token, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, path, token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private async Task<Dictionary<string, string>> FileModes(string root, string sha, string token, CancellationToken ct)
    {
        using var commit = await Get($"{root}/git/commits/{sha}", token, ct);
        var treeSha = commit.RootElement.GetProperty("tree").GetProperty("sha").GetString();
        using var tree = await Get($"{root}/git/trees/{treeSha}?recursive=1", token, ct);
        if (tree.RootElement.GetProperty("truncated").GetBoolean())
            throw new InvalidOperationException("PR file types cannot be verified from a truncated tree.");
        return tree.RootElement.GetProperty("tree").EnumerateArray().ToDictionary(
            entry => entry.GetProperty("path").GetString()!, entry => entry.GetProperty("mode").GetString()!, StringComparer.Ordinal);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, new Uri("https://api.github.com/" + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("ScrumPilot/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static string RepositoryPath(string owner, string repository)
    {
        static bool Valid(string segment) => segment.Length is > 0 and <= 100
            && segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            && segment is not "." and not "..";
        if (!Valid(owner) || !Valid(repository)) throw new ArgumentException("A GitHub owner and repository name are required, not a URL.");
        return $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}";
    }
}
