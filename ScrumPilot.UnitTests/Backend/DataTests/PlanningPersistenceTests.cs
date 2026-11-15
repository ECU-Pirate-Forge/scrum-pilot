using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Migrations.Organization;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.UnitTests.Backend.Tenancy;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class PlanningPersistenceTests
{
    private const string PreviousMigration = "20261110141300_InitialSqlOrganization";

    [Fact]
    public async Task Migration_FreshSqlCatalogSupportsUpDownUpWithoutCentralModelsOrSeedData()
    {
        await using var db = await TestDatabase.CreateAsync(migrate: false);
        await using var context = db.Context();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.Projects.ToListAsync());
        Assert.Empty(await context.Stories.ToListAsync());
        Assert.Empty(await context.ProjectGraphStates.ToListAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] IN ('AspNetUsers', 'Organizations', 'PullRequestScopeAttempts', 'GitHubWebhookDeliveries')")
            .SingleAsync());
        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = 'TaskSpecificationRevisions'").SingleAsync());
        await migrator.MigrateAsync();
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await context.TaskSpecificationRevisions.ToListAsync());
        await AssertConstraintsValidAsync(context);
    }

    [Fact]
    public async Task Migration_BackfillsValidEdgesAndRetainsInvalidLegacyIntent()
    {
        await using var db = await TestDatabase.CreateAsync(migrate: false);
        await using var context = db.Context();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await context.Database.ExecuteSqlRawAsync("""
            SET IDENTITY_INSERT [Project] ON;
            INSERT INTO "Project" ("ProjectId", "OrganizationId", "ProjectName") VALUES
                (11, 1, 'First'), (12, 1, 'Second');
            SET IDENTITY_INSERT [Project] OFF;
            SET IDENTITY_INSERT [Stories] ON;
            INSERT INTO "Stories" ("PbiId", "ProjectId", "Type", "Title", "Description", "Status",
                "Priority", "StoryPoints", "Origin", "IsDraft", "IsFlagged", "DateCreated", "LastUpdated", "DependsOnPbiId")
            VALUES
                (101, 11, 'Task', 'Valid', '', 'ToDo', 'Medium', 0, 'WebUserCreated', 0, 0, '2026-01-01', '2026-01-01', 102),
                (102, 11, 'Task', 'Missing', '', 'ToDo', 'Medium', 0, 'WebUserCreated', 0, 0, '2026-01-01', '2026-01-01', 999),
                (103, 11, 'Task', 'Cross', '', 'ToDo', 'Medium', 0, 'WebUserCreated', 0, 0, '2026-01-01', '2026-01-01', 105),
                (104, 11, 'Task', 'Self', '', 'ToDo', 'Medium', 0, 'WebUserCreated', 0, 0, '2026-01-01', '2026-01-01', 104),
                (105, 12, 'Task', 'Other', '', 'ToDo', 'Medium', 0, 'WebUserCreated', 0, 0, '2026-01-01', '2026-01-01', NULL);
            SET IDENTITY_INSERT [Stories] OFF;
            INSERT INTO [Comment] ([PbiId], [UserId], [Comment], [CreatedDate])
                VALUES (101, 'legacy-owner', 'Retain existing discussion', '2026-01-01');
            INSERT INTO [PbiStatusHistory] ([PbiId], [FromStatus], [ToStatus], [ChangedAt])
                VALUES (101, 'ToDo', 'InProgress', '2026-01-01');
            """);
        await migrator.MigrateAsync();
        var edge = Assert.Single(await context.PbiDependencies.ToListAsync());
        Assert.Equal((11, 101, 102), (edge.ProjectId, edge.DependentPbiId, edge.PrerequisitePbiId));
        Assert.Equal(4, await context.Stories.CountAsync(x => x.DependsOnPbiId != null));
        Assert.Equal(999, await context.Stories.Where(x => x.PbiId == 102).Select(x => x.DependsOnPbiId).SingleAsync());
        Assert.Equal(2, await context.ProjectGraphStates.CountAsync());
        Assert.All(await context.ProjectGraphStates.ToListAsync(), x => Assert.Equal(0, x.Revision));
        Assert.All(await context.Stories.ToListAsync(), x => Assert.Equal(1, x.Version));
        await AssertConstraintsValidAsync(context);
        await migrator.MigrateAsync(PreviousMigration);
        await migrator.MigrateAsync();
        Assert.Single(await context.PbiDependencies.ToListAsync());
        Assert.Equal(5, await context.Stories.CountAsync());
        Assert.Equal("Retain existing discussion", (await context.Comments.SingleAsync()).Body);
        Assert.Equal(101, (await context.PbiStatusHistories.SingleAsync()).PbiId);
        await AssertConstraintsValidAsync(context);
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
            new PlanningArtifactReview { ProjectId = project, PbiId = a, ActorId = "owner", Reason = "Reviewed", Fingerprint = new string('a', 64) },
            new PlanningArtifactReview { ProjectId = other, PbiId = foreign, ActorId = "owner", Reason = "Retain", Fingerprint = new string('b', 64) },
            new ProjectGuidelinesRevision { ProjectId = other, Revision = 1, Content = "Retain", ActorId = "owner" });
        await context.SaveChangesAsync();
        (await context.Projects.FindAsync(project))!.CurrentGuidelinesRevisionId = revision.ProjectGuidelinesRevisionId;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "ProjectGuidelinesRevisions" WHERE "ProjectId" = {project}"""));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "PlanningArtifactReviews" SET "Reason" = 'changed' WHERE "ProjectId" = {project}"""));
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "PlanningArtifactReviews" WHERE "ProjectId" = {project}"""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.PurgeProjectPlanningAsync(project, default));
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.PurgeProjectPlanningAsync(project, default);
        // Main SQL deliberately uses NO ACTION rather than cascading work deletion.
        await context.Stories.Where(item => item.ProjectId == project).ExecuteDeleteAsync();
        await context.Projects.Where(item => item.ProjectId == project).ExecuteDeleteAsync();
        await transaction.CommitAsync();
        Assert.False(await context.TaskSpecificationRevisions.AnyAsync(item => item.ProjectId == project));
        Assert.Equal("Retain", (await context.ProjectGuidelinesRevisions.SingleAsync()).Content);
        Assert.Equal("Retain", (await context.PlanningArtifactReviews.SingleAsync()).Reason);
        Assert.Equal(0, await context.Database.SqlQueryRaw<long>(
            """SELECT COUNT_BIG(*) AS "Value" FROM "PlanningPurgeScopes" """).SingleAsync());
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
            await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                """UPDATE "TaskSpecificationRevisions" SET "ContentHash" = 'changed';"""));
            await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
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
    public async Task AllowedFiles_PreserveOrdinalDistinctPathsAndFullLength()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, a, _, _, _) = await SeedAsync(db);
        await using var context = db.Context();
        var spec = Spec(project, a);
        var paths = new[] { "src/File.cs", "src/file.cs", "src/file.cs ", "src/" + new string('a', 1020) };
        spec.AllowedFiles = paths.Select(path => new TaskAllowedFile
        {
            ProjectId = project,
            Path = path,
            Rationale = "Exact scope"
        }).ToList();
        context.Add(spec);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(paths.Order(StringComparer.Ordinal),
            (await context.TaskAllowedFiles.Select(x => x.Path).ToArrayAsync()).Order(StringComparer.Ordinal));

        var duplicate = Spec(project, a);
        duplicate.Revision = 2;
        duplicate.AllowedFiles.Add(new() { ProjectId = project, Path = "src/file.cs", Rationale = "Duplicate" });
        context.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task NullableCandidateKeys_AllowManualRowsButRejectDuplicateGeneratedIdentity()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, _, _, _, _) = await SeedAsync(db);
        await using var context = db.Context();
        var run = Run(project);
        context.Add(run);
        await context.SaveChangesAsync();
        context.Stories.AddRange(
            new() { ProjectId = project, Title = "Manual one" },
            new() { ProjectId = project, Title = "Manual two" },
            new() { ProjectId = project, Title = "Generated one", GenerationRunId = run.GenerationRunId, GenerationCandidateKey = "candidate" },
            new() { ProjectId = project, Title = "Generated ordinal", GenerationRunId = run.GenerationRunId, GenerationCandidateKey = "Candidate" });
        await context.SaveChangesAsync();
        context.Stories.Add(new() { ProjectId = project, Title = "Duplicate", GenerationRunId = run.GenerationRunId, GenerationCandidateKey = "candidate" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task PolicyLock_UsesPersistedTenantIdentityAndTransactionOwnership()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, other, _, _, _, _) = await SeedAsync(db);
        await using var first = db.Context();
        var metadata = await first.TenantMetadata.SingleAsync();
        var storageKey = metadata.StorageKey;
        // A forged tracked identity must never choose the resource name.
        metadata.StorageKey = new string('f', 32);
        await using (var transaction = await first.Database.BeginTransactionAsync())
        {
            await first.AcquirePlanningPolicyLockAsync(project, default);
            Assert.Equal("Exclusive", await first.Database.SqlQueryRaw<string>(
                "SELECT APPLOCK_MODE('public', {0}, 'Transaction') AS [Value]",
                $"planning:{storageKey}:{project}").SingleAsync());
            Assert.Equal("NoLock", await first.Database.SqlQueryRaw<string>(
                "SELECT APPLOCK_MODE('public', {0}, 'Transaction') AS [Value]",
                $"planning:{metadata.StorageKey}:{project}").SingleAsync());
            await using var independent = db.Context();
            await using var independentTransaction = await independent.Database.BeginTransactionAsync();
            await independent.AcquirePlanningPolicyLockAsync(other, default).WaitAsync(TimeSpan.FromSeconds(10));
            await independentTransaction.RollbackAsync();
            await transaction.RollbackAsync();
        }
        await using var second = db.Context();
        await using var nextTransaction = await second.Database.BeginTransactionAsync();
        await second.AcquirePlanningPolicyLockAsync(project, default).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task PolicyLock_CanceledSqlWaitPreservesCancellationContract()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, _, _, _, _) = await SeedAsync(db);
        await using var holder = db.Context();
        await using var heldTransaction = await holder.Database.BeginTransactionAsync();
        await holder.AcquirePlanningPolicyLockAsync(project, default);
        await using var waiter = db.Context();
        await using var waitingTransaction = await waiter.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            waiter.AcquirePlanningPolicyLockAsync(project, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        if (error.InnerException is not null)
            Assert.IsType<SqlException>(error.InnerException);
    }

    [Fact]
    public async Task PurgeRollback_RetainsHistoryAndDoesNotLeakAuthorization()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, _, a, _, _, _) = await SeedAsync(db);
        await using var context = db.Context();
        context.AddRange(Spec(project, a),
            new ProjectGuidelinesRevision { ProjectId = project, Revision = 1, Content = "History", ActorId = "owner" });
        await context.SaveChangesAsync();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await context.PurgeProjectPlanningAsync(project, default);
            await transaction.RollbackAsync();
        }
        Assert.Single(await context.ProjectGuidelinesRevisions.ToListAsync());
        Assert.Single(await context.TaskSpecificationRevisions.ToListAsync());
        Assert.Equal(0, await context.Database.SqlQueryRaw<long>(
            "SELECT COUNT_BIG(*) AS [Value] FROM [PlanningPurgeScopes]").SingleAsync());
        await Assert.ThrowsAsync<SqlException>(() => context.ProjectGuidelinesRevisions.ExecuteDeleteAsync());
    }

    [Fact]
    public async Task HistoryTriggers_RejectForgedScopeAndMixedProjectMutation()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (project, other, _, _, _, _) = await SeedAsync(db);
        await using (var context = db.Context())
        {
            context.ProjectGuidelinesRevisions.AddRange(
                new() { ProjectId = project, Revision = 1, Content = "First", ActorId = "owner" },
                new() { ProjectId = other, Revision = 1, Content = "Second", ActorId = "owner" });
            await context.SaveChangesAsync();
        }
        await using (var forged = db.Context())
        {
            await using var transaction = await forged.Database.BeginTransactionAsync();
            await forged.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [PlanningPurgeScopes] ([ProjectId], [TransactionId]) VALUES ({project}, CURRENT_TRANSACTION_ID())");
            await Assert.ThrowsAsync<SqlException>(() => forged.ProjectGuidelinesRevisions
                .Where(x => x.ProjectId == project).ExecuteDeleteAsync());
            await transaction.RollbackAsync();
        }
        await using (var mixed = db.Context())
        {
            var key = await mixed.TenantMetadata.Select(x => x.StorageKey).SingleAsync();
            await using var transaction = await mixed.Database.BeginTransactionAsync();
            await mixed.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = {$"planning-purge:{key}:{project}"},
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction';
                INSERT INTO [PlanningPurgeScopes] ([ProjectId], [TransactionId]) VALUES ({project}, CURRENT_TRANSACTION_ID());
                """);
            await Assert.ThrowsAsync<SqlException>(() => mixed.ProjectGuidelinesRevisions.ExecuteDeleteAsync());
            await transaction.RollbackAsync();
        }
        await using var verify = db.Context();
        Assert.Equal(2, await verify.ProjectGuidelinesRevisions.CountAsync());
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
        await AssertConstraintsValidAsync(context);
        await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync(
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
    public void SqlServerMigration_GeneratesIdentityCompositeConstraintsBackfillAndTriggers()
    {
        using var context = new OrganizationContextFactory().CreateDbContext([]);
        var sql = context.GetService<IMigrator>().GenerateScript(PreviousMigration);
        Assert.Contains("IDENTITY", sql);
        Assert.Contains("INSERT INTO [PbiDependencies]", sql);
        Assert.Contains("APPLOCK_MODE", sql);
        Assert.Contains("FOREIGN KEY ([ProjectId], [PrerequisitePbiId])", sql);
        Assert.Contains("ON DELETE NO ACTION", sql);
        Assert.DoesNotContain("PullRequestScopeAttempts", sql);
        Assert.DoesNotContain("GitHubWebhookDeliveries", sql);
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

    private static async Task AssertConstraintsValidAsync(OrganizationContext context)
    {
        var close = context.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS";
            await using var reader = await command.ExecuteReaderAsync();
            do { Assert.False(await reader.ReadAsync(), "SQL constraint violation found."); }
            while (await reader.NextResultAsync());
        }
        finally { if (close) await context.Database.CloseConnectionAsync(); }
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqlTestDatabase database = new(initialize: false);
        public OrganizationContext Context() => new(new DbContextOptionsBuilder<OrganizationContext>()
            .UseSqlServer(database.Configuration["ConnectionStrings:control"], sql => sql.UseCompatibilityLevel(170))
            .Options);

        public static async Task<TestDatabase> CreateAsync(bool migrate = true)
        {
            var fixture = new TestDatabase();
            using var admin = new SqlServerAdministrationContext(fixture.database.Storage);
            await admin.CreateAsync(fixture.database.Catalog, requireAbsent: true);
            if (migrate)
            {
                await using var context = fixture.Context();
                await context.Database.MigrateAsync();
                context.TenantMetadata.Add(new() { OrganizationId = 1, StorageKey = Guid.NewGuid().ToString("N") });
                await context.SaveChangesAsync();
            }
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            using var admin = new SqlServerAdministrationContext(database.Storage);
            await admin.DropAsync(database.Catalog, default);
            database.Control.Dispose();
        }
    }
}
