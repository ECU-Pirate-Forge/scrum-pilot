using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Tenancy;

public sealed class TenantUnavailableException : Exception
{
    public TenantUnavailableException() : base("Organization storage is unavailable.") { }
    public TenantUnavailableException(Exception innerException) : base("Organization storage is unavailable.", innerException) { }
}

public sealed class TenantSelection
{
    public int? OrganizationId { get; set; }
}

/// <summary>Only registry-derived catalogs are opened. Runtime never creates catalogs.</summary>
public sealed class TenantStorage
{
    public SqlServerOptions Options { get; }
    private readonly ILoggerFactory? loggerFactory;
    internal ILoggerFactory? LoggerFactory => loggerFactory;
    private readonly ConcurrentDictionary<int, StorageGate> gates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> operations = new();
    public async Task<IDisposable> AcquireOperationAsync(int organizationId, CancellationToken ct)
    {
        var semaphore = operations.GetOrAdd(organizationId, _ => new(1, 1));
        await semaphore.WaitAsync(ct);
        return new OperationLease(semaphore);
    }
    private sealed class OperationLease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
    public TenantStorage(IConfiguration configuration, ILoggerFactory? loggerFactory = null, IHostEnvironment? environment = null)
    {
        Options = SqlServerOptions.FromConfiguration(configuration, environment?.EnvironmentName);
        this.loggerFactory = loggerFactory;
    }
    public static string Catalog(string key)
    {
        if (!Guid.TryParseExact(key, "N", out _)) throw new TenantUnavailableException();
        return "scrumpilot_org_" + key.ToLowerInvariant();
    }
    public string OrganizationConnectionString(string key) =>
        new SqlConnectionStringBuilder(Options.RuntimeConnectionString) { InitialCatalog = Catalog(key) }.ConnectionString;

    public string BackupPath(string key, string backupId)
    {
        if (!Guid.TryParseExact(key, "N", out _) || !Guid.TryParseExact(backupId, "N", out _))
            throw new TenantUnavailableException();
        return Checked(Path.Combine(Options.BackupApplicationPath, key + "_" + backupId + ".bak"));
    }
    public string BackupServerPath(string key, string backupId)
    {
        _ = BackupPath(key, backupId);
        return Options.BackupServerPath + "/" + key + "_" + backupId + ".bak";
    }
    public string Checked(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Options.BackupApplicationPath) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new TenantUnavailableException();
        ValidatePath(full);
        return full;
    }
    private static void ValidatePath(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new TenantUnavailableException();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public IDisposable Acquire(int organizationId)
    {
        return gates.GetOrAdd(organizationId, _ => new()).Enter(exclusive: true);
    }
    public IDisposable AcquireExclusiveSqlLease(string key)
    {
        var gate = new SqlServerCoordinationContext(Options.AdminConnectionString, loggerFactory);
        try { gate.Acquire(key, exclusive: true); return gate; }
        catch { gate.Dispose(); throw; }
    }
    private sealed class CombinedLease(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose() { try { second.Dispose(); } finally { first.Dispose(); } }
    }
    // Non-thread-affine leases: EF contexts may be disposed on a different continuation.
    // Waiting writers block new readers so maintenance drains rather than starving.
    private sealed class StorageGate
    {
        private readonly object sync = new();
        private int readers;
        private int waitingWriters;
        private bool writer;
        public IDisposable Enter(bool exclusive)
        {
            lock (sync)
            {
                if (exclusive)
                {
                    waitingWriters++;
                    try
                    {
                        while (writer || readers != 0) Monitor.Wait(sync);
                        writer = true;
                    }
                    finally { waitingWriters--; }
                }
                else
                {
                    while (writer || waitingWriters != 0) Monitor.Wait(sync);
                    readers++;
                }
            }
            return new Lease(this, exclusive);
        }
        private void Exit(bool exclusive)
        {
            lock (sync)
            {
                if (exclusive) writer = false;
                else readers--;
                Monitor.PulseAll(sync);
            }
        }
        private sealed class Lease(StorageGate gate, bool exclusive) : IDisposable
        {
            private int disposed;
            public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) gate.Exit(exclusive); }
        }
    }
    public OrganizationContext Open(Organization organization)
    {
        if (organization.DeletedAt != null || organization.StorageState != "Active" || organization.RestorePending)
            throw new TenantUnavailableException();
        IDisposable lease = gates.GetOrAdd(organization.OrganizationId, _ => new()).Enter(exclusive: false);
        try
        {
            var sqlGate = new SqlServerCoordinationContext(Options.RuntimeConnectionString, loggerFactory);
            try { sqlGate.Acquire(organization.StorageKey, exclusive: false); }
            catch { sqlGate.Dispose(); throw; }
            lease = new CombinedLease(lease, sqlGate);
            using var directory = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>()
                .UseSqlServer(Options.ControlConnectionString, sql => sql.UseCompatibilityLevel(170)).Options);
            var current = directory.Organizations.AsNoTracking().SingleOrDefault(x => x.OrganizationId == organization.OrganizationId);
            if (current == null || current.StorageKey != organization.StorageKey
                || current.DeletedAt != null || current.StorageState != "Active" || current.RestorePending)
                throw new TenantUnavailableException();
            var context = OpenFile(organization.StorageKey);
            try
            {
                Verify(context, organization);
                context.StorageLease = lease;
                return context;
            }
            catch { context.Dispose(); throw; }
        }
        catch { lease.Dispose(); throw new TenantUnavailableException(); }
    }
    public OrganizationContext OpenFile(string key)
    {
        var options = new DbContextOptionsBuilder<OrganizationContext>()
            .UseSqlServer(OrganizationConnectionString(key), sql => sql.UseCompatibilityLevel(SqlServerOptions.CompatibilityLevel));
        if (loggerFactory != null) options.UseLoggerFactory(loggerFactory);
        var context = new OrganizationContext(options.Options);
        try
        {
            context.Database.OpenConnection();
            VerifyServer(context);
            return context;
        }
        catch { context.Dispose(); throw new TenantUnavailableException(); }
    }
    public static void Verify(OrganizationContext context, Organization organization, bool allowPendingSchema = false)
    {
        var metadata = context.TenantMetadata.AsNoTracking().Single();
        if (metadata.Id != 1 || metadata.OrganizationId != organization.OrganizationId
            || metadata.StorageKey != organization.StorageKey
            || (!allowPendingSchema && !context.Database.GetAppliedMigrations().SequenceEqual(context.Database.GetMigrations())))
            throw new TenantUnavailableException();
        VerifyServer(context);
    }

    public static void VerifyServer(DbContext context)
    {
        var valid = context.Database.SqlQueryRaw<int>(
            "SELECT CAST(CASE WHEN CAST(SERVERPROPERTY('ProductMajorVersion') AS int) = 17 AND compatibility_level = 170 THEN 1 ELSE 0 END AS int) AS [Value] FROM sys.databases WHERE name = DB_NAME()").Single();
        if (valid != 1) throw new TenantUnavailableException();
    }
}
