using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class PlanningPersistenceTests
{
    private const string PreviousMigration = "20261003024314_AddOrganizationTenancy";

    [Fact]
    public async Task Migration_BackfillsValidEdgesAndRetainsInvalidLegacyIntent()
    {
        await using var db = await TestDatabase.CreateAsync(migrate: false);
        await using var context = db.Context();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Project" ("ProjectId", "OrganizationId", "ProjectName") VALUES
                (11, 1, 'First'), (12, 1, 'Second');
            INSERT INTO "Stories" ("PbiId", "ProjectId", "Type", "Title", "Description", "Status",
                "Priority", "StoryPoints", "Origin", "IsDraft", "IsFlagged", "DateCreated", "LastUpdated", "DependsOnPbiId")
            VALUES
                (101, 11, 'Task', 'Valid', '', 'New', 'Medium', 0, 'Manual', 0, 0, '2026-01-01', '2026-01-01', 102),
                (102, 11, 'Task', 'Missing', '', 'New', 'Medium', 0, 'Manual', 0, 0, '2026-01-01', '2026-01-01', 999),
                (103, 11, 'Task', 'Cross', '', 'New', 'Medium', 0, 'Manual', 0, 0, '2026-01-01', '2026-01-01', 105),
                (104, 11, 'Task', 'Self', '', 'New', 'Medium', 0, 'Manual', 0, 0, '2026-01-01', '2026-01-01', 104),
                (105, 12, 'Task', 'Other', '', 'New', 'Medium', 0, 'Manual', 0, 0, '2026-01-01', '2026-01-01', NULL);
            """);
        await migrator.MigrateAsync();
        var edge = Assert.Single(await context.PbiDependencies.ToListAsync());
        Assert.Equal((11, 101, 102), (edge.ProjectId, edge.DependentPbiId, edge.PrerequisitePbiId));
        Assert.Equal(4, await context.Stories.CountAsync(x => x.DependsOnPbiId != null));
        Assert.Equal(999, await context.Stories.Where(x => x.PbiId == 102).Select(x => x.DependsOnPbiId).SingleAsync());
        Assert.Equal(2, await context.ProjectGraphStates.CountAsync());
        Assert.All(await context.ProjectGraphStates.ToListAsync(), x => Assert.Equal(0, x.Revision));
        Assert.Equal(0L, await context.Database.SqlQueryRaw<long>(
            """SELECT COUNT(*) AS "Value" FROM pragma_foreign_key_check""").SingleAsync());
        await migrator.MigrateAsync(PreviousMigration);
        await migrator.MigrateAsync();
        Assert.Single(await context.PbiDependencies.ToListAsync());
    }

    [Fact]
    public async Task AuthorizedPurgeRemovesScopedHistory_ButOrdinaryDeletionIsRejected()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, other, a, _, _, foreign) = await SeedAsync(db);
        await using var context = db.Context();
        var revision = new ProjectGuidelinesRevision { ProjectId = project, Revision = 1, Content = "History", ActorId = "owner" };
        var repository = new ProjectRepository { ProjectId = project, GitHubRepositoryId = 99, InstallationId = 42, Owner = "owner", Name = "repository", DefaultBranch = "main" };
        context.Add(repository);
        await context.SaveChangesAsync();
        context.AddRange(revision, Spec(project, a),
            new PullRequestScopeAttempt { ProjectId = project, ProjectRepositoryId = repository.ProjectRepositoryId, PullRequestNumber = 7, HeadSha = new string('a', 40) },
            new PlanningArtifactReview { ProjectId = project, PbiId = a, ActorId = "owner", Reason = "Reviewed", Fingerprint = new string('a', 64) },
            new PlanningArtifactReview { ProjectId = other, PbiId = foreign, ActorId = "owner", Reason = "Retain", Fingerprint = new string('b', 64) },
            new ProjectGuidelinesRevision { ProjectId = other, Revision = 1, Content = "Retain", ActorId = "owner" });
        await context.SaveChangesAsync();
        (await context.Projects.FindAsync(project))!.CurrentGuidelinesRevisionId = revision.ProjectGuidelinesRevisionId;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "ProjectGuidelinesRevisions" WHERE "ProjectId" = {project}"""));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "PlanningArtifactReviews" SET "Reason" = 'changed' WHERE "ProjectId" = {project}"""));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "PlanningArtifactReviews" WHERE "ProjectId" = {project}"""));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "PullRequestScopeAttempts" SET "HeadSha" = 'changed' WHERE "ProjectId" = {project}"""));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "PullRequestScopeAttempts" WHERE "ProjectId" = {project}"""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.PurgeProjectPlanningAsync(project, default));
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.PurgeProjectPlanningAsync(project, default);
        await context.Projects.Where(item => item.ProjectId == project).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        Assert.False(await context.TaskSpecificationRevisions.AnyAsync(item => item.ProjectId == project));
        Assert.False(await context.PullRequestScopeAttempts.AnyAsync());
        Assert.Equal("Retain", (await context.ProjectGuidelinesRevisions.SingleAsync()).Content);
        Assert.Equal("Retain", (await context.PlanningArtifactReviews.SingleAsync()).Reason);
        Assert.Equal(0, await context.Database.SqlQueryRaw<long>(
            """SELECT COUNT(*) AS "Value" FROM "PlanningPurgeScopes" """).SingleAsync());
    }

    [Fact]
    public async Task GraphStateAlone_DoesNotPreventProjectDeletion()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var context = db.Context();
        var project = new Project { OrganizationId = 1, ProjectName = "Empty" };
        context.Add(project);
        await context.SaveChangesAsync();
        context.ProjectGraphStates.Add(new() { ProjectId = project.ProjectId });
        await context.SaveChangesAsync();
        context.Remove(project);
        await context.SaveChangesAsync();
        Assert.Empty(await context.ProjectGraphStates.ToListAsync());
    }

    [Fact]
    public async Task Edges_AllowMultiplePrerequisitesButRejectSelfDuplicatesCrossProjectAndDeletion()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, other, a, b, c, foreign) = await SeedAsync(db);
        await using (var context = db.Context())
        {
            context.PbiDependencies.AddRange(
                new() { ProjectId = project, DependentPbiId = a, PrerequisitePbiId = b },
                new() { ProjectId = project, DependentPbiId = a, PrerequisitePbiId = c });
            await context.SaveChangesAsync();
            Assert.Equal(2, await context.PbiDependencies.CountAsync());
        }
        await AssertEdgeRejected(project, a, b);
        await AssertEdgeRejected(project, a, a);
        await AssertEdgeRejected(project, a, foreign);
        await AssertEdgeRejected(other, a, foreign);
        await using var deleting = db.Context();
        deleting.Stories.Remove((await deleting.Stories.FindAsync(b))!);
        await Assert.ThrowsAsync<DbUpdateException>(() => deleting.SaveChangesAsync());

        async Task AssertEdgeRejected(int p, int dependent, int prerequisite)
        {
            await using var context = db.Context();
            context.PbiDependencies.Add(new() { ProjectId = p, DependentPbiId = dependent, PrerequisitePbiId = prerequisite });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ProvenanceAndHierarchy_RejectCrossProjectReferences()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, other, a, _, _, foreign) = await SeedAsync(db);
        await using (var context = db.Context())
        {
            var pbi = (await context.Stories.FindAsync(a))!;
            pbi.ParentStoryId = foreign;
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await using (var context = db.Context())
        {
            var problem = new ProblemStatement { ProjectId = other, Title = "Problem", Text = "Text" };
            context.ProblemStatements.Add(problem);
            await context.SaveChangesAsync();
            var pbi = (await context.Stories.FindAsync(a))!;
            pbi.ProblemStatementId = problem.ProblemStatementId;
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await using (var context = db.Context())
        {
            var revision = new ProjectGuidelinesRevision { ProjectId = other, Revision = 1, Content = "Other", ActorId = "test" };
            context.ProjectGuidelinesRevisions.Add(revision);
            await context.SaveChangesAsync();
            (await context.Projects.FindAsync(project))!.CurrentGuidelinesRevisionId = revision.ProjectGuidelinesRevisionId;
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Revisions_HashRoundTripAndRejectMutationIncludingRawSql()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, a, _, _, _) = await SeedAsync(db);
        int specId;
        await using (var context = db.Context())
        {
            var guideline = new ProjectGuidelinesRevision { ProjectId = project, Revision = 1, Content = "Guidance", ActorId = "test" };
            var spec = Spec(project, a);
            context.AddRange(guideline, spec);
            await context.SaveChangesAsync();
            specId = spec.TaskSpecificationRevisionId;
            Assert.Equal(64, guideline.ContentHash.Length);
            Assert.Equal(64, spec.ContentHash.Length);
            Assert.Single(await context.TaskAllowedFiles.ToListAsync());
            guideline.Content = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
        await using (var context = db.Context())
        {
            var spec = await context.TaskSpecificationRevisions.Include(x => x.AllowedFiles).SingleAsync();
            Assert.Equal("Implement", spec.Content.Goal);
            Assert.Equal(new[] { "Pass tests" }, spec.Content.AcceptanceCriteria);
            spec.Content.AcceptanceCriteria[0] = "Changed in place";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
        await using (var context = db.Context())
        {
            await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
                """UPDATE "TaskSpecificationRevisions" SET "ContentHash" = 'changed';"""));
            await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
                """DELETE FROM "ProjectGuidelinesRevisions";"""));
            context.TaskAllowedFiles.Add(new()
            {
                ProjectId = project,
                TaskSpecificationRevisionId = specId,
                Path = "extra.cs",
                Rationale = "Later"
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("/absolute")]
    [InlineData("src/*.cs")]
    [InlineData("src\\file.cs")]
    [InlineData("C:/absolute")]
    [InlineData("src//file.cs")]
    public async Task AllowedFiles_RejectNonExactPaths(string path)
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, a, _, _, _) = await SeedAsync(db);
        await using var context = db.Context();
        var spec = Spec(project, a);
        spec.AllowedFiles.Single().Path = path;
        context.Add(spec);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Generation_IdempotencyIsUniqueWithinProjectAndKind()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, other, _, _, _, _) = await SeedAsync(db);
        await using (var context = db.Context())
        {
            context.GenerationRuns.AddRange(Run(project), Run(other), Run(project, GenerationKind.Tasks));
            await context.SaveChangesAsync();
        }
        await using var duplicate = db.Context();
        duplicate.GenerationRuns.Add(Run(project));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact]
    public async Task Generation_AllowsStateChangesButNotProvenanceChanges()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, _, _, _, _) = await SeedAsync(db);
        await using var context = db.Context();
        var run = Run(project);
        context.GenerationRuns.Add(run);
        await context.SaveChangesAsync();
        run.State = GenerationState.Succeeded;
        run.CompletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        Assert.Equal(2, run.Version);
        run.Provider = "different";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task RepositorySnapshotsInputsBindingsAndScopeChecks_PersistWithRestrictedSources()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, a, _, _, _) = await SeedAsync(db);
        await using var context = db.Context();
        var repository = new ProjectRepository
        {
            ProjectId = project,
            GitHubRepositoryId = 123,
            InstallationId = 456,
            Owner = "example",
            Name = "example",
            DefaultBranch = "main"
        };
        var run = Run(project);
        context.AddRange(repository, run);
        await context.SaveChangesAsync();
        var snapshot = new RepositorySnapshot
        {
            ProjectId = project,
            ProjectRepositoryId = repository.ProjectRepositoryId,
            CommitSha = new('a', 40),
            TreeSha = new('b', 40),
            TreeJson = "[]"
        };
        context.Add(snapshot);
        await context.SaveChangesAsync();
        var input = new GenerationInput
        {
            ProjectId = project,
            GenerationRunId = run.GenerationRunId,
            RepositorySnapshotId = snapshot.RepositorySnapshotId,
            SnapshotJson = """{"prompt":"test"}"""
        };
        var spec = Spec(project, a);
        spec.GenerationRunId = run.GenerationRunId;
        spec.RepositorySnapshotId = snapshot.RepositorySnapshotId;
        spec.AllowedFiles.Single().Path = "src/[id]/file.cs";
        var binding = new TaskPullRequestBinding
        {
            ProjectId = project,
            PbiId = a,
            ProjectRepositoryId = repository.ProjectRepositoryId,
            PullRequestNumber = 1
        };
        context.AddRange(input, spec, binding);
        await context.SaveChangesAsync();
        var check = new PullRequestScopeCheck
        {
            ProjectId = project,
            TaskPullRequestBindingId = binding.TaskPullRequestBindingId,
            TaskSpecificationRevisionId = spec.TaskSpecificationRevisionId,
            HeadSha = snapshot.CommitSha,
            State = ScopeCheckState.Passed,
            ResultJson = """{"violations":[]}"""
        };
        context.Add(check);
        await context.SaveChangesAsync();
        Assert.Equal(64, input.ContentHash.Length);
        Assert.Equal(ScopeCheckState.Passed, (await context.PullRequestScopeChecks.SingleAsync()).State);
        Assert.Equal(0L, await context.Database.SqlQueryRaw<long>(
            """SELECT COUNT(*) AS "Value" FROM pragma_foreign_key_check""").SingleAsync());
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "ProjectRepositories" WHERE "ProjectRepositoryId" = {repository.ProjectRepositoryId}"""));
    }

    [Fact]
    public async Task PortableConcurrency_RejectsStalePbiAndGraphWrites()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, a, _, _, _) = await SeedAsync(db);
        await using var first = db.Context();
        await using var second = db.Context();
        var pbi1 = (await first.Stories.FindAsync(a))!;
        var pbi2 = (await second.Stories.FindAsync(a))!;
        pbi1.Title = "First";
        await first.SaveChangesAsync();
        Assert.Equal(2, pbi1.Version);
        pbi2.Title = "Second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        second.ChangeTracker.Clear();
        var graph1 = (await first.ProjectGraphStates.FindAsync(project))!;
        var graph2 = (await second.ProjectGraphStates.FindAsync(project))!;
        first.Entry(graph1).Property(x => x.Revision).IsModified = true;
        second.Entry(graph2).Property(x => x.Revision).IsModified = true;
        await first.SaveChangesAsync();
        Assert.Equal(1, graph1.Revision);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public void PostgreSqlMigration_GeneratesIdentityCompositeConstraintsBackfillAndTriggers()
    {
        using var context = new ScrumPilotContext(new DbContextOptionsBuilder<ScrumPilotContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var sql = context.GetService<IMigrator>().GenerateScript(PreviousMigration);
        Assert.Contains("GENERATED BY DEFAULT AS IDENTITY", sql);
        Assert.Contains("INSERT INTO \"PbiDependencies\"", sql);
        Assert.Contains("planning_reject_mutation", sql);
        Assert.Contains("FOREIGN KEY (\"ProjectId\", \"PrerequisitePbiId\")", sql);
        Assert.Contains("ON DELETE RESTRICT", sql);
    }

    private static GenerationRun Run(int project, GenerationKind kind = GenerationKind.Stories) => new()
    {
        ProjectId = project,
        Kind = kind,
        Provider = "test",
        IdempotencyKey = "same-key",
        ActorId = "test"
    };

    private static TaskSpecificationRevision Spec(int project, int pbi) => new()
    {
        ProjectId = project,
        PbiId = pbi,
        Revision = 1,
        ActorId = "test",
        Content = new() { Goal = "Implement", AcceptanceCriteria = ["Pass tests"] },
        AllowedFiles = [new() { ProjectId = project, Path = "src/file.cs", Rationale = "Implementation" }]
    };

    private static async Task<(int Project, int Other, int A, int B, int C, int Foreign)> SeedAsync(TestDatabase db)
    {
        await using var context = db.Context();
        var project = new Project { OrganizationId = 1, ProjectName = "First" };
        var other = new Project { OrganizationId = 1, ProjectName = "Second" };
        context.Projects.AddRange(project, other);
        await context.SaveChangesAsync();
        var items = Enumerable.Range(0, 4).Select(i => new ProductBacklogItem
        {
            ProjectId = i == 3 ? other.ProjectId : project.ProjectId,
            Title = $"Item {i}",
            Type = PbiType.Task
        }).ToArray();
        context.Stories.AddRange(items);
        context.ProjectGraphStates.Add(new() { ProjectId = project.ProjectId });
        await context.SaveChangesAsync();
        return (project.ProjectId, other.ProjectId, items[0].PbiId, items[1].PbiId, items[2].PbiId, items[3].PbiId);
    }

    private sealed class TestDatabase(SqliteConnection connection) : IAsyncDisposable
    {
        public ScrumPilotContext Context() => new(new DbContextOptionsBuilder<ScrumPilotContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

        public static async Task<TestDatabase> CreateAsync(bool migrate = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var database = new TestDatabase(connection);
            if (migrate)
            {
                await using var context = database.Context();
                await context.Database.MigrateAsync();
            }
            return database;
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
