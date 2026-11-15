using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Migrations.Organization;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.Tenancy;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.DataTests;

/// <summary>Exercises the current SQL model and migration guards without replacing migration round-trip coverage.</summary>
public sealed class PlanningSqlSchemaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectDeletion_RetiresUnderPolicyLock_BeforePurgeAndRollsBackOnFailure(bool failRetirement)
    {
        var fixture = new SqlTestDatabase(initialize: false);
        using var admin = new SqlServerAdministrationContext(fixture.Storage);
        var controlCatalog = fixture.Catalog + "_control";
        await admin.CreateAsync(fixture.Catalog, requireAbsent: true);
        try
        {
            await admin.CreateAsync(controlCatalog, requireAbsent: true);
            await using var control = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>()
                .UseSqlServer(new SqlConnectionStringBuilder(SqlTestDatabase.Connection)
                { InitialCatalog = controlCatalog }.ConnectionString, sql => sql.UseCompatibilityLevel(170)).Options);
            await control.Database.EnsureCreatedAsync();
            await using var db = new OrganizationContext(new DbContextOptionsBuilder<OrganizationContext>()
                .UseSqlServer(fixture.Configuration["ConnectionStrings:control"], sql => sql.UseCompatibilityLevel(170)).Options);
            await db.Database.EnsureCreatedAsync();
            foreach (var statement in OrganizationPlanningSql.GuardStatements())
                await db.Database.ExecuteSqlRawAsync(statement);
            var key = Guid.NewGuid().ToString("N");
            db.TenantMetadata.Add(new() { OrganizationId = 1, StorageKey = key });
            var project = new Project { OrganizationId = 1, ProjectName = "Delete" };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            db.Stories.Add(new() { ProjectId = project.ProjectId, Title = "Work" });
            db.ProjectGuidelinesRevisions.Add(new() { ProjectId = project.ProjectId, Revision = 1, Content = "History", ActorId = "test" });
            await db.SaveChangesAsync();
            var observer = new RetirementProbe(db, key, failRetirement);
            var repository = new TenantProjectAccessRepository(db, control,
                new TenantSelection { OrganizationId = 1 }, [observer]);
            if (failRetirement)
                await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAsync(project.ProjectId));
            else
                Assert.True(await repository.DeleteAsync(project.ProjectId));
            Assert.True(observer.Called);
            Assert.Equal(failRetirement, await db.Projects.AnyAsync());
            Assert.Equal(failRetirement, await db.Stories.AnyAsync());
            Assert.Equal(failRetirement, await db.ProjectGuidelinesRevisions.AnyAsync());
        }
        finally
        {
            fixture.Control.Dispose();
            await admin.DropAsync(controlCatalog, default);
            await admin.DropAsync(fixture.Catalog, default);
        }
    }

    [Fact]
    public async Task CurrentSchema_HistoryGuardsAndScopedPurge_ExecuteOnSqlServer()
    {
        var fixture = new SqlTestDatabase(initialize: false);
        using var admin = new SqlServerAdministrationContext(fixture.Storage);
        await admin.CreateAsync(fixture.Catalog, requireAbsent: true);
        try
        {
            await using var db = new OrganizationContext(new DbContextOptionsBuilder<OrganizationContext>()
                .UseSqlServer(fixture.Configuration["ConnectionStrings:control"], sql => sql.UseCompatibilityLevel(170)).Options);
            await db.Database.EnsureCreatedAsync();
            foreach (var statement in OrganizationPlanningSql.GuardStatements())
                await db.Database.ExecuteSqlRawAsync(statement);
            db.TenantMetadata.Add(new() { OrganizationId = 1, StorageKey = Guid.NewGuid().ToString("N") });
            var project = new Project { OrganizationId = 1, ProjectName = "Smoke" };
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            var pbi = new ProductBacklogItem { ProjectId = project.ProjectId, Title = "Task", Type = PbiType.Task };
            db.Stories.Add(pbi);
            await db.SaveChangesAsync();
            var revision = new ProjectGuidelinesRevision { ProjectId = project.ProjectId, Revision = 1, Content = "History", ActorId = "test" };
            var spec = new TaskSpecificationRevision
            {
                ProjectId = project.ProjectId,
                PbiId = pbi.PbiId,
                Revision = 1,
                ActorId = "test",
                Content = new() { Goal = "Smoke" },
                AllowedFiles = new[] { "src/File.cs", "src/file.cs", "src/file.cs ", "src/" + new string('a', 1020) }
                    .Select(p => new TaskAllowedFile { ProjectId = project.ProjectId, Path = p, Rationale = "Exact" }).ToList()
            };
            db.AddRange(revision, spec);
            await db.SaveChangesAsync();
            Assert.Equal(4, await db.TaskAllowedFiles.CountAsync());
            await Assert.ThrowsAsync<SqlException>(() => db.ProjectGuidelinesRevisions.ExecuteDeleteAsync());
            await Assert.ThrowsAsync<SqlException>(() => db.ProjectGuidelinesRevisions.ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.Content, "changed")));
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.AcquirePlanningPolicyLockAsync(project.ProjectId, default);
                await db.PurgeProjectPlanningAsync(project.ProjectId, default);
                Assert.Empty(await db.ProjectGuidelinesRevisions.ToListAsync());
                await transaction.RollbackAsync();
            }
            Assert.Single(await db.ProjectGuidelinesRevisions.ToListAsync());
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.PurgeProjectPlanningAsync(project.ProjectId, default);
                await transaction.CommitAsync();
            }
            Assert.Empty(await db.TaskAllowedFiles.ToListAsync());
            Assert.Empty(await db.ProjectGuidelinesRevisions.ToListAsync());
        }
        finally
        {
            fixture.Control.Dispose();
            await admin.DropAsync(fixture.Catalog, default);
        }

    }

    private sealed class RetirementProbe(OrganizationContext db, string storageKey, bool fail) : IProjectPlanningRetirement
    {
        public bool Called { get; private set; }
        public async Task RetireProjectAsync(int organizationId, int projectId, CancellationToken ct)
        {
            Assert.Equal(1, organizationId);
            Assert.NotNull(db.Database.CurrentTransaction);
            Assert.Equal("Exclusive", await db.Database.SqlQueryRaw<string>(
                "SELECT APPLOCK_MODE('public', {0}, 'Transaction') AS [Value]",
                $"planning:{storageKey}:{projectId}").SingleAsync(ct));
            Assert.True(await db.ProjectGuidelinesRevisions.AnyAsync(x => x.ProjectId == projectId, ct));
            Called = true;
            if (fail) throw new InvalidOperationException("Retirement failed before tenant purge.");
        }
    }
}
