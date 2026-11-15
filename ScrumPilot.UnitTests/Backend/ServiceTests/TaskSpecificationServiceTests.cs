using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class TaskSpecificationServiceTests
{
    [Fact]
    public async Task SaveAndApprove_AppendRevisionsAndPreserveExactManifestIncludingTests()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var draft = await f.SaveDraftAsync();
        var draftHash = draft.ContentHash;
        Assert.False((await f.Specs.StatusAsync(f.ProjectId, f.TaskId, default)).IsCurrent);

        var approved = await f.Specs.ApproveAsync(f.ProjectId, f.TaskId, 1, "owner", default);
        Assert.NotEqual(draft.TaskSpecificationRevisionId, approved.TaskSpecificationRevisionId);
        Assert.Equal(2, approved.Revision);
        Assert.Equal("owner", approved.ApprovedByActorId);
        Assert.NotNull(approved.ApprovedAt);
        Assert.Equal(TaskScopeMode.Enforced, approved.ScopeMode);
        Assert.Equal(f.Snapshot.RepositorySnapshotId, approved.RepositorySnapshotId);
        Assert.Equal(f.Project.CurrentGuidelinesRevisionId, approved.ProjectGuidelinesRevisionId);
        Assert.Contains("/repos/owner/repository/commits/release/planning", f.Http.Requests);
        Assert.DoesNotContain("/repos/owner/repository/commits/main", f.Http.Requests);

        f.Db.ChangeTracker.Clear();
        var revisions = await f.Db.TaskSpecificationRevisions.AsNoTracking().Include(x => x.AllowedFiles)
            .OrderBy(x => x.Revision).ToArrayAsync();
        Assert.Equal(2, revisions.Length);
        Assert.Equal(SpecificationApproval.Draft, revisions[0].Approval);
        Assert.Null(revisions[0].ApprovedByActorId);
        Assert.Null(revisions[0].ApprovedAt);
        Assert.Equal(draftHash, revisions[0].ContentHash);
        Assert.Equal(SpecificationApproval.Approved, revisions[1].Approval);
        // Record equality compares the array properties by reference after hydration.
        // Compare the entire persisted payload, including every ordered array, instead.
        Assert.Equal(JsonSerializer.Serialize(revisions[0].Content), JsonSerializer.Serialize(revisions[1].Content));
        Assert.Equal(f.Manifest, revisions[1].AllowedFiles.OrderBy(x => x.Path, StringComparer.Ordinal)
            .Select(TaskSpecificationService.Proposal).ToArray());
        Assert.Equal(revisions[0].AllowedFiles.Count, revisions[1].AllowedFiles.Count);
        Assert.Empty(revisions[0].AllowedFiles.Select(x => x.TaskAllowedFileId)
            .Intersect(revisions[1].AllowedFiles.Select(x => x.TaskAllowedFileId)));
        Assert.True((await f.Specs.StatusAsync(f.ProjectId, f.TaskId, default)).IsCurrent);
        var repeated = await f.Specs.ApproveAsync(f.ProjectId, f.TaskId, 2, "owner", default);
        Assert.Equal(approved.TaskSpecificationRevisionId, repeated.TaskSpecificationRevisionId);
        Assert.Equal(2, await f.Db.TaskSpecificationRevisions.CountAsync());
    }

    [Theory]
    [InlineData("source", false)]
    [InlineData("source", true)]
    [InlineData("guidance", false)]
    [InlineData("guidance", true)]
    public async Task ChangedInputs_PreventCurrencyAndApproval(string changed, bool approveFirst)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.SaveDraftAsync();
        if (approveFirst) await f.Specs.ApproveAsync(f.ProjectId, f.TaskId, 1, "owner", default);
        await f.ChangeInputAsync(changed);

        var status = await f.Specs.StatusAsync(f.ProjectId, f.TaskId, default);
        Assert.False(status.IsCurrent);
        Assert.Contains(status.ReviewReasons, reason => reason.Contains(changed == "source" ? "source" : "guidance",
            StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Specs.ApproveAsync(f.ProjectId, f.TaskId, approveFirst ? 2 : 1, "owner", default));
        Assert.Equal(approveFirst ? 2 : 1, await f.Db.TaskSpecificationRevisions.CountAsync());
    }

    [Fact]
    public async Task StaleExpectedRevision_RejectsSaveAndApprovalWithoutWritingHistory()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.SaveDraftAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.SaveDraftAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Specs.ApproveAsync(f.ProjectId, f.TaskId, 0, "owner", default));
        Assert.Single(await f.Db.TaskSpecificationRevisions.ToArrayAsync());
        Assert.Empty(f.Http.Requests);
    }

    [Fact]
    public async Task Approval_RejectsMovedSnapshotBranchEvenWhenDefaultBranchWasNotSelected()
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        await f.SaveDraftAsync();
        f.Http.BranchSha = TaskPlanningFixture.OtherSha;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Specs.ApproveAsync(f.ProjectId, f.TaskId, 1, "owner", default));
        Assert.Contains("branch moved", error.Message);
        Assert.Contains("/repos/owner/repository/commits/release/planning", f.Http.Requests);
        Assert.Equal(SpecificationApproval.Draft, (await f.Db.TaskSpecificationRevisions.SingleAsync()).Approval);
    }

    [Theory]
    [InlineData("missing-tests")]
    [InlineData("unknown-existing-file")]
    [InlineData("wildcard")]
    public async Task Save_RejectsUnverifiedOrIncompleteManifestBeforePersisting(string invalid)
    {
        await using var f = await TaskPlanningFixture.CreateAsync();
        var files = invalid switch
        {
            "missing-tests" => f.Manifest.Where(x => x.Category != AllowedFileCategory.Test).ToArray(),
            "unknown-existing-file" => f.Manifest.Select(x => x.Category == AllowedFileCategory.Test
                ? x with { Path = "tests/DoesNotExist.cs" } : x).ToArray(),
            _ => f.Manifest.Select(x => x.Category == AllowedFileCategory.Test
                ? x with { Path = "tests/*.cs" } : x).ToArray()
        };
        await Assert.ThrowsAsync<ArgumentException>(() => f.Specs.SaveAsync(f.ProjectId, f.TaskId,
            new(TaskPlanningFixture.Content(), files, f.Snapshot.RepositorySnapshotId, 0), "owner", default));
        Assert.Empty(await f.Db.TaskSpecificationRevisions.ToArrayAsync());
        Assert.Empty(await f.Db.TaskAllowedFiles.ToArrayAsync());
    }
}

// Shared by the three focused orchestration suites. All HTTP is handled in memory;
// unknown routes fail immediately rather than falling through to a network client.
internal sealed class TaskPlanningFixture : IAsyncDisposable
{
    public const string SnapshotSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string OtherSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    public const string HeadSha = "cccccccccccccccccccccccccccccccccccccccc";
    private readonly SqliteConnection _connection = new(
        $"Data Source=task-planning-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True;Default Timeout=1");
    private readonly HttpClient _client;
    private static readonly Lazy<string> AppKey = new(() =>
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    });

    public ScrumPilotContext Db { get; private set; } = default!;
    public PlanningGitHubHandler Http { get; } = new();
    public GitHubAppClient GitHub { get; }
    public ProjectWorkNotifier Notifier { get; } = new(Substitute.For<IHubContext<ProjectWorkHub>>(),
        new ProjectWorkSubscriptions(), Substitute.For<IOrganizationAccessService>(), NullLogger<ProjectWorkNotifier>.Instance);
    public TaskSpecificationService Specs { get; private set; } = default!;
    public Project Project { get; private set; } = default!;
    public ProductBacklogItem Story { get; private set; } = default!;
    public ProductBacklogItem TaskItem { get; private set; } = default!;
    public ProjectRepository Repository { get; private set; } = default!;
    public RepositorySnapshot Snapshot { get; private set; } = default!;
    public int ProjectId => Project.ProjectId;
    public int TaskId => TaskItem.PbiId;
    public ScrumPilotContext NewContext() =>
        new(new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(_connection.ConnectionString).Options);
    public TaskFileProposal[] Manifest { get; } =
    [
        new("docs/task.md", AllowedFileCategory.Documentation, AllowedFileOperation.Modify, "Describe the changed behavior."),
        new("src/Worker.cs", AllowedFileCategory.Source, AllowedFileOperation.Modify, "Implement the task."),
        new("tests/WorkerTests.cs", AllowedFileCategory.Test, AllowedFileOperation.Modify, "Verify the task.")
    ];

    private TaskPlanningFixture()
    {
        _client = new(Http);
        GitHub = new(_client, Options.Create(new GitHubAppOptions { AppId = "123", PrivateKeyPem = AppKey.Value }));
    }

    public static async Task<TaskPlanningFixture> CreateAsync()
    {
        var f = new TaskPlanningFixture();
        await f._connection.OpenAsync();
        f.Db = new(new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(f._connection)
            // The migrations were scaffolded for the production provider; use the same
            // SQLite migration policy as PlanningPersistenceTests.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)).Options);
        await f.Db.Database.MigrateAsync();
        var org = new Organization { Name = "Task QA", NormalizedName = "TASK QA" };
        f.Db.Organizations.Add(org);
        await f.Db.SaveChangesAsync();
        f.Project = new() { ProjectName = "Task planning", OrganizationId = org.OrganizationId };
        f.Db.Projects.Add(f.Project);
        await f.Db.SaveChangesAsync();
        var guidance = new ProjectGuidelinesRevision
        {
            ProjectId = f.ProjectId,
            Revision = 1,
            Content = "Use focused tests.",
            ActorId = "owner"
        };
        f.Db.ProjectGuidelinesRevisions.Add(guidance);
        await f.Db.SaveChangesAsync();
        f.Project.CurrentGuidelinesRevisionId = guidance.ProjectGuidelinesRevisionId;
        f.Repository = new()
        {
            ProjectId = f.ProjectId,
            GitHubRepositoryId = 99,
            InstallationId = 42,
            Owner = "owner",
            Name = "repository",
            DefaultBranch = "main"
        };
        f.Story = new()
        {
            ProjectId = f.ProjectId,
            Title = "Parent requirements",
            Description = "Implement verified work.",
            Type = PbiType.Story,
            Status = PbiStatus.ToDo
        };
        f.Db.AddRange(f.Repository, f.Story);
        await f.Db.SaveChangesAsync();
        f.TaskItem = new()
        {
            ProjectId = f.ProjectId,
            Title = "Implement work",
            Type = PbiType.Task,
            ParentStoryId = f.Story.PbiId,
            Status = PbiStatus.ToDo
        };
        f.Snapshot = new()
        {
            ProjectId = f.ProjectId,
            ProjectRepositoryId = f.Repository.ProjectRepositoryId,
            CommitSha = SnapshotSha,
            TreeSha = "tree",
            Branch = "release/planning",
            TreeJson = JsonSerializer.Serialize(new VerifiedRepositoryTree(99, "release/planning", SnapshotSha, "tree", f.Http.Entries))
        };
        f.Db.AddRange(f.TaskItem, f.Snapshot);
        await f.Db.SaveChangesAsync();
        f.Specs = new(f.Db, f.GitHub, f.Notifier);
        return f;
    }

    public Task<TaskSpecificationRevision> SaveDraftAsync(int expectedRevision = 0) =>
        Specs.SaveAsync(ProjectId, TaskId, new(Content(), Manifest, Snapshot.RepositorySnapshotId, expectedRevision), "author", default);

    public async Task ApproveAsync()
    {
        await SaveDraftAsync();
        await Specs.ApproveAsync(ProjectId, TaskId, 1, "owner", default);
    }

    public async Task ChangeInputAsync(string changed)
    {
        if (changed == "source")
            Story.Description = "Changed acceptance criteria.";
        else
        {
            var guidance = new ProjectGuidelinesRevision
            {
                ProjectId = ProjectId,
                Revision = 2,
                Content = "New review rules.",
                ActorId = "owner"
            };
            Db.ProjectGuidelinesRevisions.Add(guidance);
            await Db.SaveChangesAsync();
            Project.CurrentGuidelinesRevisionId = guidance.ProjectGuidelinesRevisionId;
        }
        await Db.SaveChangesAsync();
    }

    public static TaskSpecificationContent Content() => new()
    {
        Goal = "Implement verified work.",
        Requirements = ["Honor the parent requirements."],
        AcceptanceCriteria = ["The worker returns the expected result."],
        VerificationSteps = ["dotnet test --filter WorkerTests; all selected tests pass."],
        OutOfScope = ["No unrelated refactoring."],
        CurrentContext = "src/Worker.cs contains the current worker.",
        ImplementationSteps = ["Update the worker and its tests."],
        Documentation = ["Update docs/task.md."],
        DefinitionOfDone = ["All selected tests pass and scope is reviewed."]
    };

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

internal sealed class PlanningGitHubHandler : HttpMessageHandler
{
    public string BranchSha { get; set; } = TaskPlanningFixture.SnapshotSha;
    public string MergeBaseSha { get; set; } = TaskPlanningFixture.SnapshotSha;
    public string BaseSha { get; set; } = TaskPlanningFixture.SnapshotSha;
    public int? ChangedFilesCount { get; set; }
    public HttpStatusCode MetadataStatus { get; set; } = HttpStatusCode.OK;
    public int[] OpenPullRequestNumbers { get; set; } = [7];
    public Func<JsonElement, CancellationToken, Task>? BeforeCheckResponse { get; set; }
    public List<string> Requests { get; } = [];
    public List<JsonElement> Checks { get; } = [];
    public PullRequestFile[] Files { get; set; } =
    [
        new("src/Worker.cs", "modified", null),
        new("tests/WorkerTests.cs", "modified", null),
        new("docs/task.md", "modified", null)
    ];
    public RepositoryTreeEntry[] Entries { get; } =
        new[] { "src/Worker.cs", "src/Renamed.cs", "tests/WorkerTests.cs", "tests/UnrelatedTests.cs",
            "docs/task.md", "docs/unrelated.md" }
        .Select(path => new RepositoryTreeEntry(path, "blob", "100644", new string('d', 40))).ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal("api.github.com", request.RequestUri!.Host);
        var path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath);
        Requests.Add(path);
        object response;
        if (request.Method == HttpMethod.Post && path == "/app/installations/42/access_tokens")
            response = new { token = "test-only-installation-token" };
        else if (request.Method == HttpMethod.Get && path == "/repos/owner/repository")
            response = new { id = 99 };
        else if (request.Method == HttpMethod.Get && path == "/repos/owner/repository/commits/release/planning")
            response = new { sha = BranchSha, commit = new { tree = new { sha = "tree" } } };
        else if (request.Method == HttpMethod.Get && path.StartsWith("/repos/owner/repository/git/commits/", StringComparison.Ordinal))
            response = new { tree = new { sha = "tree" } };
        else if (request.Method == HttpMethod.Get && path == "/repos/owner/repository/git/trees/tree")
            response = new { truncated = false, tree = Entries.Select(e => new { path = e.Path, type = e.Type, mode = e.Mode, sha = e.Sha }) };
        else if (request.Method == HttpMethod.Get && path.StartsWith("/repos/owner/repository/git/blobs/", StringComparison.Ordinal))
            response = new { size = 16, encoding = "base64", content = Convert.ToBase64String("class Worker { }"u8.ToArray()) };
        else if (request.Method == HttpMethod.Get && path == "/repos/owner/repository/pulls/7")
        {
            if (MetadataStatus != HttpStatusCode.OK)
                return new(MetadataStatus) { Content = JsonContent.Create(new { message = "Synthetic metadata outage." }) };
            response = new
            {
                number = 7,
                state = "open",
                changed_files = ChangedFilesCount ?? Files.Length,
                head = new { sha = TaskPlanningFixture.HeadSha },
                @base = new { sha = BaseSha }
            };
        }
        else if (request.Method == HttpMethod.Get && path == "/repos/owner/repository/pulls"
                 && request.RequestUri.Query == "?state=open&per_page=100&page=1")
            response = OpenPullRequestNumbers.Select(number => new
            { number, state = "open", head = new { sha = TaskPlanningFixture.HeadSha } });
        else if (request.Method == HttpMethod.Get && path == "/repos/owner/repository/pulls/7/files")
            response = Files.Select(file => new { filename = file.Path, status = file.Status, previous_filename = file.PreviousPath });
        else if (request.Method == HttpMethod.Get && path ==
                 $"/repos/owner/repository/compare/{BaseSha}...{TaskPlanningFixture.HeadSha}")
            response = new { merge_base_commit = new { sha = MergeBaseSha } };
        else if (request.Method == HttpMethod.Post && path == "/repos/owner/repository/check-runs")
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Checks.Add(json.RootElement.Clone());
            if (BeforeCheckResponse is not null)
                await BeforeCheckResponse(json.RootElement.Clone(), cancellationToken);
            response = new { id = 1000 + Checks.Count };
        }
        else throw new InvalidOperationException($"Unexpected fake GitHub request: {request.Method} {path}");
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
    }
}
