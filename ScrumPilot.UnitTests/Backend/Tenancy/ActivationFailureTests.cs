using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

public sealed class ActivationFailureTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Restore_ActivationFailureKeepsRolledBackGrantsFenced(bool afterCommit, bool cancellation)
    {
        await VerifyFailureAsync(restore: true, afterCommit, cancellation);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Upgrade_ActivationFailurePreservesRestoreFence(bool afterCommit, bool cancellation)
    {
        await VerifyFailureAsync(restore: false, afterCommit, cancellation);
    }

    private static async Task VerifyFailureAsync(bool restore, bool afterCommit, bool cancellation)
    {
        using var files = new TenantStorageTests();
        var org = await files.Create("ActivationFailure");
        var projectId = await files.Project(org, "Snapshot");
        var membership = new OrganizationMembership
        {
            OrganizationId = org.OrganizationId,
            UserId = "member",
            Role = OrganizationRole.Member
        };
        files.Control.OrganizationMemberships.Add(membership);
        await files.Control.SaveChangesAsync();
        using (var tenant = files.Storage.Open(org))
        {
            tenant.ProjectMemberships.Add(new()
            {
                ProjectId = projectId,
                UserId = "member",
                GrantedByUserId = "owner",
                MembershipInstanceId = membership.MembershipInstanceId
            });
            await tenant.SaveChangesAsync();
        }
        var backup = await files.Operations.BackupAsync(org.OrganizationId);
        using (var tenant = files.Storage.Open(org))
            await tenant.Projects.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProjectName, "Live"));
        if (!restore)
        {
            org.RestorePending = true; org.StorageState = "Maintenance";
            await files.Control.SaveChangesAsync();
        }

        using var cancellationSource = new CancellationTokenSource();
        var sawClearedGrants = false;
        var sawCommittedActivation = false;
        var fault = new ActivationFault(afterCommit, cancellation, cancellationSource, async () =>
        {
            using var tenant = files.Storage.OpenFile(org.StorageKey);
            sawClearedGrants = !await tenant.ProjectMemberships.AnyAsync();
            using var inspection = OpenControl(files);
            var persisted = await inspection.Organizations.SingleAsync();
            sawCommittedActivation = persisted.StorageState == "Active" && !persisted.RestorePending;
        });
        using var failingControl = new ControlPlaneContext(
            new DbContextOptionsBuilder<ControlPlaneContext>()
                .UseSqlServer(files.Storage.Options.ControlConnectionString, sql => sql.UseCompatibilityLevel(170))
                .AddInterceptors(fault).Options);
        var operations = new TenantOperations(failingControl, files.Storage);
        await Assert.ThrowsAsync<TenantUnavailableException>(() => restore
            ? operations.RestoreAsync(org.OrganizationId, backup, cancellationSource.Token)
            : operations.UpgradeAsync(org.OrganizationId, cancellationSource.Token));
        Assert.True(fault.Triggered);
        Assert.True(sawClearedGrants);
        Assert.Equal(afterCommit, sawCommittedActivation);

        using var recovery = OpenControl(files);
        var fenced = await recovery.Organizations.SingleAsync();
        Assert.Equal("Maintenance", fenced.StorageState);
        Assert.True(fenced.RestorePending);
        var tracked = failingControl.Organizations.Local.Single();
        Assert.Equal("Maintenance", tracked.StorageState);
        Assert.True(tracked.RestorePending);
        Assert.Throws<TenantUnavailableException>(() => files.Storage.Open(fenced));
        Assert.Throws<TenantUnavailableException>(() => files.Storage.Open(org));
        using (var tenant = files.Storage.OpenFile(org.StorageKey))
        {
            Assert.Equal("Live", (await tenant.Projects.SingleAsync()).ProjectName);
            if (restore)
                Assert.Equal(membership.MembershipInstanceId,
                    (await tenant.ProjectMemberships.SingleAsync()).MembershipInstanceId);
        }

        await new TenantOperations(recovery, files.Storage).UpgradeAsync(org.OrganizationId);
        using var reopened = files.Storage.Open(fenced);
        Assert.Empty(await reopened.ProjectMemberships.ToListAsync());
        Assert.False(fenced.RestorePending);
        Assert.Equal("Active", fenced.StorageState);
    }

    private static ControlPlaneContext OpenControl(TenantStorageTests files) =>
        new(new DbContextOptionsBuilder<ControlPlaneContext>()
            .UseSqlServer(files.Storage.Options.ControlConnectionString, sql => sql.UseCompatibilityLevel(170)).Options);

    private sealed class ActivationFault(bool afterCommit, bool cancellation,
        CancellationTokenSource source, Func<Task> inspect) : SaveChangesInterceptor
    {
        private bool armed;
        public bool Triggered { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Triggered && eventData.Context!.ChangeTracker.Entries<Organization>().Any(entry =>
                entry.Entity.StorageState == "Active" && !entry.Entity.RestorePending
                && entry.Property(x => x.StorageState).OriginalValue == "Maintenance"))
            {
                armed = true;
                if (!afterCommit) await FailAsync();
            }
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (armed && !Triggered && afterCommit) await FailAsync();
            return result;
        }

        private async Task FailAsync()
        {
            Triggered = true;
            await inspect();
            if (cancellation)
            {
                source.Cancel();
                source.Token.ThrowIfCancellationRequested();
            }
            throw new TimeoutException("Injected activation acknowledgement failure.");
        }
    }
}
