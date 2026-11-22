using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

public sealed class TenantStorageTests : IDisposable
{
    internal readonly SqlTestDatabase Database = new();
    internal TenantStorage Storage => Database.Storage;
    internal ControlPlaneContext Control => Database.Control;
    internal TenantOperations Operations => Database.Operations;
    public TenantStorageTests()
    {
        Control.Users.AddRange(new ApplicationUser { Id = "owner", UserName = "owner", NormalizedUserName = "OWNER", EmailConfirmed = true },
            new ApplicationUser { Id = "member", UserName = "member", NormalizedUserName = "MEMBER", EmailConfirmed = true },
            new ApplicationUser { Id = "admin", UserName = "admin", NormalizedUserName = "ADMIN", EmailConfirmed = true });
        Control.SaveChanges();
    }
    internal async Task<Organization> Create(string name, string owner = "owner")
    {
        var org = new Organization { Name = name, NormalizedName = name.ToUpperInvariant(), CreatedAt = DateTime.UtcNow };
        Control.Organizations.Add(org); await Control.SaveChangesAsync();
        Control.OrganizationMemberships.Add(new() { OrganizationId = org.OrganizationId, UserId = owner, Role = OrganizationRole.Owner });
        await Control.SaveChangesAsync(); await Operations.ProvisionAsync(org.OrganizationId);
        return org;
    }
    internal async Task<int> Project(Organization org, string name = "Work")
    {
        using var tenant = Storage.Open(org);
        var project = new Project { OrganizationId = org.OrganizationId, ProjectName = name };
        tenant.Projects.Add(project); await tenant.SaveChangesAsync(); return project.ProjectId;
    }
    [Fact]
    public async Task ModelsAndMigrations_AreSeparateAndHaveNoPendingChanges()
    {
        var org = await Create("Separate"); using var tenant = Storage.Open(org);
        Assert.False(Control.Database.HasPendingModelChanges()); Assert.False(tenant.Database.HasPendingModelChanges());
        Assert.DoesNotContain(tenant.Model.GetEntityTypes(), e => e.GetTableName()!.StartsWith("AspNet", StringComparison.Ordinal));
        Assert.DoesNotContain(Control.Model.GetEntityTypes(), e => e.ClrType == typeof(Project));
        Assert.All(tenant.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()),
            fk => Assert.DoesNotContain("AspNet", fk.PrincipalEntityType.GetTableName()!));
        TenantStorage.VerifyServer(Control); TenantStorage.VerifyServer(tenant);
    }
    [Fact]
    public async Task CollidingProjectIds_AreIndependent()
    {
        var a = await Create("A"); var b = await Create("B");
        Assert.Equal(await Project(a, "A"), await Project(b, "B"));
        using var first = Storage.Open(a); using var second = Storage.Open(b);
        Assert.Equal("A", (await first.Projects.SingleAsync()).ProjectName);
        Assert.Equal("B", (await second.Projects.SingleAsync()).ProjectName);
    }
    [Fact]
    public async Task Provisioning_IsIdempotent_AndDoesNotAdoptMismatchedMetadata()
    {
        var org = await Create("Idempotent"); await Project(org);
        await Operations.ProvisionAsync(org.OrganizationId);
        using (var tenant = Storage.OpenFile(org.StorageKey))
        {
            Assert.Single(await tenant.Projects.ToListAsync());
        }
        var mismatched = new Organization
        {
            Name = "Mismatched",
            NormalizedName = "MISMATCHED",
            CreatedAt = DateTime.UtcNow,
            StorageState = "Active"
        };
        Control.Organizations.Add(mismatched);
        await Control.SaveChangesAsync();
        using var admin = new SqlServerAdministrationContext(Storage);
        await admin.CreateAsync(TenantStorage.Catalog(mismatched.StorageKey), requireAbsent: true);
        using (var tenant = admin.OpenOrganization(mismatched.StorageKey))
        {
            await tenant.Database.MigrateAsync();
            tenant.TenantMetadata.Add(new()
            {
                OrganizationId = mismatched.OrganizationId + 1,
                StorageKey = mismatched.StorageKey
            });
            await tenant.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<TenantUnavailableException>(() => Operations.ProvisionAsync(mismatched.OrganizationId));
        Assert.Throws<TenantUnavailableException>(() => Storage.Open(mismatched));
    }
    [Fact]
    public async Task MissingCatalog_IsNeverRecreatedByOrdinaryOpen()
    {
        var org = await Create("Lost");
        using var admin = new SqlServerAdministrationContext(Storage);
        await admin.DropAsync(TenantStorage.Catalog(org.StorageKey), default);
        Assert.Throws<TenantUnavailableException>(() => Storage.Open(org));
        Assert.False(await admin.ExistsAsync(TenantStorage.Catalog(org.StorageKey)));
    }
    [Fact]
    public async Task ReaderLeasesAllowOrdinaryConcurrency_AndMaintenanceDrains()
    {
        var org = await Create("Drain");
        using var reader = Storage.Open(org);
        using (var other = Storage.Open(org)) Assert.True(other.HasStorageLease);
        org.StorageState = "Maintenance"; await Control.SaveChangesAsync();
        Assert.Throws<TenantUnavailableException>(() => Storage.Open(org));
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(() => { using var lease = Storage.Acquire(org.OrganizationId); acquired.SetResult(); });
        await Task.Delay(50); Assert.False(acquired.Task.IsCompleted);
        reader.Dispose(); await writer.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Fact]
    public async Task SqlBackupRestore_RestoresRows_AndClearsMemberGrants()
    {
        var org = await Create("Restore"); var projectId = await Project(org);
        var member = new OrganizationMembership { OrganizationId = org.OrganizationId, UserId = "member", Role = OrganizationRole.Member };
        Control.OrganizationMemberships.Add(member); await Control.SaveChangesAsync();
        using (var tenant = Storage.Open(org))
        {
            tenant.ProjectMemberships.Add(new() { ProjectId = projectId, UserId = "member", GrantedByUserId = "owner", MembershipInstanceId = member.MembershipInstanceId });
            tenant.Stories.Add(new() { ProjectId = projectId, Title = "Original" }); await tenant.SaveChangesAsync();
        }
        var backup = await Operations.BackupAsync(org.OrganizationId);
        using (var tenant = Storage.Open(org)) await tenant.Stories.ExecuteDeleteAsync();
        await Operations.RestoreAsync(org.OrganizationId, backup);
        using var restored = Storage.Open(org);
        Assert.Equal("Original", (await restored.Stories.SingleAsync()).Title);
        Assert.Empty(await restored.ProjectMemberships.ToListAsync());
        Assert.False(org.RestorePending); Assert.Equal("Active", org.StorageState);
        Assert.Equal(2, await Control.TenantBackups.CountAsync());
    }
    [Fact]
    public async Task RenameCatalog_PreservesWorkAndRestoresPreRenameBackup()
    {
        var org = await Create("Rename " + Guid.NewGuid().ToString("N"));
        await Project(org, "Before rename");
        var backup = await Operations.BackupAsync(org.OrganizationId);
        var oldCatalog = TenantStorage.Catalog(org.StorageKey);
        await Operations.RenameCatalogAsync(org.OrganizationId);
        using var admin = new SqlServerAdministrationContext(Storage);
        Assert.False(await admin.ExistsAsync(oldCatalog));
        Assert.Equal(TenantStorage.CatalogFromName(org.Name), org.CatalogName);
        Assert.False(org.CatalogRenamePending);
        using (var tenant = Storage.Open(org))
        {
            Assert.Equal("Before rename", (await tenant.Projects.SingleAsync()).ProjectName);
            await tenant.Projects.ExecuteDeleteAsync();
        }
        await Operations.RestoreAsync(org.OrganizationId, backup);
        using var restored = Storage.Open(org);
        Assert.Equal("Before rename", (await restored.Projects.SingleAsync()).ProjectName);
    }

    [Fact]
    public async Task InterruptedCatalogRename_RemainsFencedAndCanResume()
    {
        var org = await Create("Retry " + Guid.NewGuid().ToString("N"));
        var interrupted = new TenantOperations(Control, Storage, [new FailingRenameObserver()]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.RenameCatalogAsync(org.OrganizationId));
        Assert.True(org.CatalogRenamePending);
        Assert.Equal("Maintenance", org.StorageState);
        Assert.Throws<TenantUnavailableException>(() => Storage.Open(org));
        await Assert.ThrowsAsync<TenantUnavailableException>(() => Operations.UpgradeAsync(org.OrganizationId));
        await Operations.RenameCatalogAsync(org.OrganizationId);
        Assert.False(org.CatalogRenamePending);
        using var tenant = Storage.Open(org);
        TenantStorage.Verify(tenant, org);
    }

    [Fact]
    public async Task CatalogRename_ResumesAfterSqlRenameAlreadyCommitted()
    {
        var org = await Create("Committed " + Guid.NewGuid().ToString("N"));
        var source = TenantStorage.Catalog(org.StorageKey);
        org.CatalogName = TenantStorage.CatalogFromName(org.Name);
        org.CatalogRenamePending = true;
        org.StorageState = "Maintenance";
        await Control.SaveChangesAsync();
        using (var admin = new SqlServerAdministrationContext(Storage))
            await admin.RenameAsync(source, org.CatalogName, default);
        await Operations.RenameCatalogAsync(org.OrganizationId);
        Assert.False(org.CatalogRenamePending);
        using var tenant = Storage.Open(org);
        TenantStorage.Verify(tenant, org);
    }

    private sealed class FailingRenameObserver : ITenantLifecycleObserver
    {
        public Task OnDrainedAsync(int organizationId, string storageKey, TenantLifecycleOperation operation, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated interruption.");
    }
    [Fact]
    public async Task InterruptedRestore_ThenControlledMigrate_ClearsOldGrantsBeforeReactivation()
    {
        var org = await Create("Recovery"); var project = await Project(org);
        using (var tenant = Storage.Open(org))
        {
            tenant.ProjectMemberships.Add(new() { ProjectId = project, UserId = "member", GrantedByUserId = "owner", MembershipInstanceId = Guid.NewGuid() });
            await tenant.SaveChangesAsync();
        }
        org.RestorePending = true; org.StorageState = "Maintenance"; await Control.SaveChangesAsync();
        await Operations.UpgradeAsync(org.OrganizationId);
        using var recovered = Storage.Open(org);
        Assert.Empty(await recovered.ProjectMemberships.ToListAsync()); Assert.False(org.RestorePending);
    }
    [Fact]
    public async Task Restore_PersistsFenceBeforeEviction_AndFailureCannotReactivate()
    {
        var org = await Create("Fence"); await Project(org);
        var backup = await Operations.BackupAsync(org.OrganizationId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Operations.RestoreAsync(org.OrganizationId, backup,
            evictConnections: _ =>
            {
                using var independent = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>()
                    .UseSqlServer(Storage.Options.ControlConnectionString).Options);
                var persisted = independent.Organizations.Single();
                Assert.True(persisted.RestorePending); Assert.Equal("Maintenance", persisted.StorageState);
                throw new InvalidOperationException("Simulated interruption");
            }));
        Assert.True(org.RestorePending); Assert.Throws<TenantUnavailableException>(() => Storage.Open(org));
        await Operations.UpgradeAsync(org.OrganizationId); Assert.False(org.RestorePending);
    }
    [Fact]
    public async Task BackupTamperingAndCrossOrganizationArtifacts_AreRejectedWithoutChangingWork()
    {
        var a = await Create("BackupA"); var b = await Create("BackupB"); await Project(a);
        var backup = await Operations.BackupAsync(a.OrganizationId);
        await Assert.ThrowsAsync<TenantUnavailableException>(() => Operations.RestoreAsync(b.OrganizationId, backup));
        await File.AppendAllTextAsync(Storage.BackupPath(a.StorageKey, backup), "tampered");
        await Assert.ThrowsAsync<TenantUnavailableException>(() => Operations.RestoreAsync(a.OrganizationId, backup));
        Assert.Equal("Active", a.StorageState); using var tenant = Storage.Open(a); Assert.Single(await tenant.Projects.ToListAsync());
    }
    [Fact]
    public async Task Purge_RequiresThirtyDays_DeletesManagedBackupsAndIsIdempotent()
    {
        var org = await Create("Purge"); await Project(org); var backup = await Operations.BackupAsync(org.OrganizationId);
        org.DeletedAt = DateTime.UtcNow; await Control.SaveChangesAsync();
        Assert.False(await Operations.PurgeAsync(org.OrganizationId, org.DeletedAt.Value.AddDays(29)));
        Assert.True(await Operations.PurgeAsync(org.OrganizationId, org.DeletedAt.Value.AddDays(30)));
        Assert.False(File.Exists(Storage.BackupPath(org.StorageKey, backup)));
        Assert.Empty(await Control.TenantBackups.ToListAsync());
        using var admin = new SqlServerAdministrationContext(Storage);
        Assert.False(await admin.ExistsAsync(TenantStorage.Catalog(org.StorageKey)));
        Assert.True(await Operations.PurgeAsync(org.OrganizationId, DateTime.UtcNow.AddDays(31)));
    }
    [Fact]
    public async Task SqlRowVersion_RejectsStaleOrganizationUpdates()
    {
        var org = await Create("Concurrency");
        using var other = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>().UseSqlServer(Storage.Options.ControlConnectionString).Options);
        var stale = await other.Organizations.SingleAsync();
        org.Name = "First"; await Control.SaveChangesAsync();
        stale.Name = "Stale";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
    }
    [Fact]
    public async Task StatusHistoryAndAssignee_ArePreservedAcrossRestore()
    {
        var org = await Create("History"); var project = await Project(org);
        using (var tenant = Storage.Open(org))
        {
            var story = new ProductBacklogItem { ProjectId = project, Title = "History", AssignedToUserId = "owner", Status = PbiStatus.ToDo };
            tenant.Stories.Add(story); await tenant.SaveChangesAsync();
            story.Status = PbiStatus.InProgress; await tenant.SaveChangesAsync();
            Assert.Single(await tenant.PbiStatusHistories.ToListAsync());
        }
        var backup = await Operations.BackupAsync(org.OrganizationId); await Operations.RestoreAsync(org.OrganizationId, backup);
        using var restored = Storage.Open(org);
        Assert.Equal("owner", (await restored.Stories.SingleAsync()).AssignedToUserId);
        Assert.Single(await restored.PbiStatusHistories.ToListAsync());
    }
    public void Dispose() => Database.Dispose();
}
