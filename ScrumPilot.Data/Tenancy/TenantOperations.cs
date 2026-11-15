using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Tenancy;

public sealed record TenantBackupManifest(int OrganizationId, string StorageKey, string BackupId,
    string Sha256, string[] Migrations);

/// <summary>Durable operation states fence work before eviction and exclusive draining.</summary>
public sealed class TenantOperations(ControlPlaneContext control, TenantStorage storage,
    IEnumerable<ITenantLifecycleObserver>? observers = null)
{
    private async Task NotifyDrainedAsync(Organization organization, TenantLifecycleOperation operation, CancellationToken ct)
    {
        foreach (var observer in observers ?? [])
            await observer.OnDrainedAsync(organization.OrganizationId, organization.StorageKey, operation, ct);
    }
    public async Task ProvisionAsync(int organizationId, CancellationToken ct = default)
    {
        using var operation = await storage.AcquireOperationAsync(organizationId, ct);
        var org = await control.Organizations.SingleAsync(x => x.OrganizationId == organizationId, ct);
        if (org.DeletedAt != null || org.StorageState is "Purged" or "Purging" or "Maintenance")
            throw new TenantUnavailableException();
        using var admin = new SqlServerAdministrationContext(storage);
        if (org.StorageState == "Active")
        {
            using var existing = admin.OpenOrganization(org.StorageKey);
            TenantStorage.Verify(existing, org);
            await admin.GrantRuntimeAccessAsync(TenantStorage.Catalog(org.StorageKey), storage.Options.RuntimeConnectionString, ct);
            await ActivateAsync(org, existing, ct);
            return;
        }
        org.StorageState = "Provisioning"; org.OperationError = null;
        await control.SaveChangesAsync(ct);
        using var lease = storage.Acquire(organizationId);
        using var sqlLease = storage.AcquireExclusiveSqlLease(org.StorageKey);
        try
        {
            await admin.CreateAsync(TenantStorage.Catalog(org.StorageKey), ct: ct);
            using var tenant = admin.OpenOrganization(org.StorageKey);
            await tenant.Database.MigrateAsync(ct);
            var metadata = await tenant.TenantMetadata.SingleOrDefaultAsync(ct);
            if (metadata is null)
            {
                if (await tenant.Projects.AnyAsync(ct)) throw new TenantUnavailableException();
                tenant.TenantMetadata.Add(new() { OrganizationId = org.OrganizationId, StorageKey = org.StorageKey });
                await tenant.SaveChangesAsync(ct);
            }
            TenantStorage.Verify(tenant, org);
            await admin.GrantRuntimeAccessAsync(TenantStorage.Catalog(org.StorageKey), storage.Options.RuntimeConnectionString, ct);
            await ActivateAsync(org, tenant, ct);
        }
        catch
        {
            org.StorageState = "Failed";
            org.OperationError = "Provisioning failed; retry the operation.";
            await control.SaveChangesAsync(CancellationToken.None);
            throw new TenantUnavailableException();
        }
    }

    public async Task<string> BackupAsync(int organizationId, CancellationToken ct = default)
    {
        using var operation = await storage.AcquireOperationAsync(organizationId, ct);
        var org = await control.Organizations.AsNoTracking().SingleAsync(x => x.OrganizationId == organizationId, ct);
        using var tenant = storage.Open(org);
        return await BackupCoreAsync(org, tenant, ct);
    }

    private async Task<string> BackupCoreAsync(Organization org, OrganizationContext tenant, CancellationToken ct)
    {
        TenantStorage.Verify(tenant, org, allowPendingSchema: true);
        var id = Guid.NewGuid().ToString("N");
        var path = storage.BackupPath(org.StorageKey, id);
        Directory.CreateDirectory(storage.Options.BackupApplicationPath);
        using var admin = new SqlServerAdministrationContext(storage);
        await admin.BackupAsync(TenantStorage.Catalog(org.StorageKey), storage.BackupServerPath(org.StorageKey, id), ct);
        var hash = await HashAsync(path, ct);
        control.TenantBackups.Add(new()
        {
            BackupId = id,
            OrganizationId = org.OrganizationId,
            StorageKey = org.StorageKey,
            Sha256 = hash,
            MigrationsJson = JsonSerializer.Serialize((await tenant.Database.GetAppliedMigrationsAsync(ct)).ToArray()),
            CreatedAt = DateTime.UtcNow
        });
        await control.SaveChangesAsync(ct);
        return id;
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    public async Task RestoreAsync(int organizationId, string backupId, CancellationToken ct = default,
        Func<CancellationToken, Task>? evictConnections = null)
    {
        using var operation = await storage.AcquireOperationAsync(organizationId, ct);
        var org = await control.Organizations.SingleAsync(x => x.OrganizationId == organizationId, ct);
        if (org.DeletedAt != null || org.StorageState is not ("Active" or "Maintenance"))
            throw new TenantUnavailableException();
        var path = storage.BackupPath(org.StorageKey, backupId);
        var registered = await control.TenantBackups.AsNoTracking().SingleOrDefaultAsync(x =>
            x.BackupId == backupId && x.OrganizationId == organizationId && x.StorageKey == org.StorageKey, ct)
            ?? throw new TenantUnavailableException();
        if (registered.Sha256 != await HashAsync(path, ct)) throw new TenantUnavailableException();
        using var admin = new SqlServerAdministrationContext(storage);
        await admin.VerifyBackupAsync(storage.BackupServerPath(org.StorageKey, backupId), ct);
        var known = new OrganizationContextFactory().CreateDbContext([]);
        using (known)
        {
            var applied = JsonSerializer.Deserialize<string[]>(registered.MigrationsJson) ?? throw new TenantUnavailableException();
            if (!applied.SequenceEqual(known.Database.GetMigrations().Take(applied.Length))) throw new TenantUnavailableException();
        }
        // Restore into a disposable isolated catalog and verify identity/schema before touching live work.
        var staging = "scrumpilot_restore_" + org.StorageKey + "_" + Guid.NewGuid().ToString("N");
        try
        {
            await admin.RestoreStagingAsync(TenantStorage.Catalog(org.StorageKey), staging,
                storage.BackupServerPath(org.StorageKey, backupId), ct);
            using var verification = admin.OpenStaging(staging);
            TenantStorage.Verify(verification, org, allowPendingSchema: true);
            if (!JsonSerializer.Deserialize<string[]>(registered.MigrationsJson)!.SequenceEqual(await verification.Database.GetAppliedMigrationsAsync(ct)))
                throw new TenantUnavailableException();
        }
        finally { await admin.DropAsync(staging, CancellationToken.None); }
        org.StorageState = "Maintenance"; org.RestorePending = true; org.OperationId = Guid.NewGuid();
        await control.SaveChangesAsync(ct);
        if (evictConnections != null) await evictConnections(ct);
        using var lease = storage.Acquire(organizationId);
        using var sqlLease = storage.AcquireExclusiveSqlLease(org.StorageKey);
        string? rollback = null;
        try
        {
            await NotifyDrainedAsync(org, TenantLifecycleOperation.Restore, ct);
            if (await admin.ExistsAsync(TenantStorage.Catalog(org.StorageKey), ct))
            {
                using var live = storage.OpenFile(org.StorageKey);
                TenantStorage.Verify(live, org, allowPendingSchema: true);
                rollback = await BackupCoreAsync(org, live, ct);
            }
            org.OperationError = rollback == null ? "Original catalog missing." : $"Rollback backup: {rollback}";
            await control.SaveChangesAsync(ct);
            await admin.RestoreAsync(TenantStorage.Catalog(org.StorageKey), storage.BackupServerPath(org.StorageKey, backupId), ct);
            using var restored = admin.OpenOrganization(org.StorageKey);
            TenantStorage.Verify(restored, org, allowPendingSchema: true);
            if (!JsonSerializer.Deserialize<string[]>(registered.MigrationsJson)!.SequenceEqual(await restored.Database.GetAppliedMigrationsAsync(ct)))
                throw new TenantUnavailableException();
            await restored.Database.MigrateAsync(ct);
            TenantStorage.Verify(restored, org);
            await admin.GrantRuntimeAccessAsync(TenantStorage.Catalog(org.StorageKey), storage.Options.RuntimeConnectionString, ct);
            await ActivateAsync(org, restored, ct);
        }
        catch
        {
            // Activation may have committed even when its acknowledgement failed.
            // Re-fence independently before restoring any backup containing old grants.
            // If this cannot be persisted, do not roll back the tenant catalog.
            await FenceAsync(org, restorePending: true, "Restore failed; recovery remains fenced.");
            var rolledBack = false;
            if (rollback != null)
            {
                try
                {
                    await admin.RestoreAsync(TenantStorage.Catalog(org.StorageKey), storage.BackupServerPath(org.StorageKey, rollback), CancellationToken.None);
                    rolledBack = true;
                }
                catch { /* Durable fence remains even if SQL recovery itself fails. */ }
            }
            await FenceAsync(org, restorePending: true,
                $"Restore failed; rollback backup: {rollback ?? "none"}; reverted: {rolledBack}.");
            throw new TenantUnavailableException();
        }
    }

    public async Task UpgradeAsync(int organizationId, CancellationToken ct = default,
        Func<CancellationToken, Task>? evictConnections = null)
    {
        using var operation = await storage.AcquireOperationAsync(organizationId, ct);
        var org = await control.Organizations.SingleAsync(x => x.OrganizationId == organizationId, ct);
        if (org.DeletedAt != null || org.StorageState is not ("Active" or "Maintenance")) throw new TenantUnavailableException();
        var restorePending = org.RestorePending;
        org.StorageState = "Maintenance"; org.OperationId = Guid.NewGuid();
        await control.SaveChangesAsync(ct);
        if (evictConnections != null) await evictConnections(ct);
        using var lease = storage.Acquire(organizationId);
        using var sqlLease = storage.AcquireExclusiveSqlLease(org.StorageKey);
        try
        {
            await NotifyDrainedAsync(org, TenantLifecycleOperation.Upgrade, ct);
            using var admin = new SqlServerAdministrationContext(storage);
            using var tenant = admin.OpenOrganization(org.StorageKey);
            TenantStorage.Verify(tenant, org, allowPendingSchema: true);
            var rollback = await BackupCoreAsync(org, tenant, ct);
            org.OperationError = $"Rollback backup: {rollback}"; await control.SaveChangesAsync(ct);
            await tenant.Database.MigrateAsync(ct);
            TenantStorage.Verify(tenant, org);
            await admin.GrantRuntimeAccessAsync(TenantStorage.Catalog(org.StorageKey), storage.Options.RuntimeConnectionString, ct);
            await ActivateAsync(org, tenant, ct);
        }
        catch
        {
            await FenceAsync(org, restorePending, "Schema upgrade failed; maintenance remains active.");
            throw new TenantUnavailableException();
        }
    }

    private async Task ActivateAsync(Organization org, OrganizationContext tenant, CancellationToken ct)
    {
        var restorePending = org.RestorePending;
        try
        {
            if (restorePending)
            {
                await tenant.ProjectMemberships.ExecuteDeleteAsync(ct);
                org.RestorePending = false;
            }
            org.StorageState = "Active";
            await control.SaveChangesAsync(ct);
        }
        catch
        {
            // Never leave an unsuccessful activation staged for a later error save.
            org.StorageState = "Maintenance";
            org.RestorePending = restorePending;
            throw;
        }
    }

    private async Task FenceAsync(Organization org, bool restorePending, string error)
    {
        var options = new DbContextOptionsBuilder<ControlPlaneContext>()
            .UseSqlServer(storage.Options.ControlConnectionString,
                sql => sql.UseCompatibilityLevel(SqlServerOptions.CompatibilityLevel));
        if (storage.LoggerFactory is { } logging) options.UseLoggerFactory(logging);
        using var recovery = new ControlPlaneContext(options.Options);
        // A fresh context/atomic ORM update handles an uncertain commit and its new
        // rowversion without replaying any failed context's tracked activation.
        var changed = await recovery.Organizations
            .Where(x => x.OrganizationId == org.OrganizationId && x.StorageKey == org.StorageKey)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.StorageState, "Maintenance")
                .SetProperty(x => x.RestorePending, x => restorePending || x.RestorePending)
                .SetProperty(x => x.OperationError, error), CancellationToken.None);
        if (changed != 1) throw new TenantUnavailableException();
        await control.Entry(org).ReloadAsync(CancellationToken.None);
    }

    public async Task<bool> PurgeAsync(int organizationId, DateTime utcNow, CancellationToken ct = default)
    {
        using var operation = await storage.AcquireOperationAsync(organizationId, ct);
        var org = await control.Organizations.SingleAsync(x => x.OrganizationId == organizationId, ct);
        if (org.StorageState == "Purged") return true;
        if (org.DeletedAt is null || utcNow < org.DeletedAt.Value.AddDays(30)) return false;
        org.StorageState = "Purging"; org.OperationId = Guid.NewGuid(); await control.SaveChangesAsync(ct);
        using var lease = storage.Acquire(organizationId);
        using var sqlLease = storage.AcquireExclusiveSqlLease(org.StorageKey);
        try
        {
            await NotifyDrainedAsync(org, TenantLifecycleOperation.Purge, ct);
            await control.Users.Where(x => x.DefaultOrganizationId == organizationId).ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.DefaultOrganizationId, (int?)null).SetProperty(x => x.DefaultProjectId, (int?)null), ct);
            using var admin = new SqlServerAdministrationContext(storage);
            await admin.DropAsync(TenantStorage.Catalog(org.StorageKey), ct);
            // Enumerate the managed namespace too: includes an artifact created before registry commit.
            foreach (var file in Directory.EnumerateFiles(storage.Options.BackupApplicationPath, org.StorageKey + "_*.bak"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length != 65 || !Guid.TryParseExact(name[33..], "N", out _)) throw new TenantUnavailableException();
                File.Delete(storage.Checked(file));
            }
            if (Directory.EnumerateFiles(storage.Options.BackupApplicationPath, org.StorageKey + "_*").Any())
                throw new TenantUnavailableException();
            foreach (var artifact in await control.TenantBackups.Where(x => x.OrganizationId == organizationId).ToListAsync(ct))
                if (File.Exists(storage.BackupPath(org.StorageKey, artifact.BackupId))
                    || await admin.BackupFileExistsAsync(storage.BackupServerPath(org.StorageKey, artifact.BackupId), ct))
                    throw new TenantUnavailableException();
            await control.TenantBackups.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            org.StorageState = "Purged"; org.OperationError = null; await control.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception exception)
        {
            org.OperationError = "Purge incomplete; retry required."; await control.SaveChangesAsync(CancellationToken.None);
            throw new TenantUnavailableException(exception);
        }
    }
}
