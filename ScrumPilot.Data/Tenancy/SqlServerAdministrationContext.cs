using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ScrumPilot.Data.Context;

namespace ScrumPilot.Data.Tenancy;

/// <summary>Privileged SQL administrative boundary. No application entities or CRUD.</summary>
public sealed class SqlServerAdministrationContext : DbContext
{
    private readonly TenantStorage storage;
    public SqlServerAdministrationContext(TenantStorage storage, ILoggerFactory? loggerFactory = null)
        : base(Options(storage, loggerFactory)) => this.storage = storage;

    private static DbContextOptions<SqlServerAdministrationContext> Options(TenantStorage storage, ILoggerFactory? loggerFactory)
    {
        var builder = new DbContextOptionsBuilder<SqlServerAdministrationContext>()
            .UseSqlServer(new SqlConnectionStringBuilder(storage.Options.AdminConnectionString)
            { InitialCatalog = "master" }.ConnectionString, sql => sql.CommandTimeout(300).UseCompatibilityLevel(170));
        if ((loggerFactory ?? storage.LoggerFactory) is { } logging) builder.UseLoggerFactory(logging);
        return builder.Options;
    }

    private static string Identifier(string name)
    {
        if (name.Length > 128 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new TenantUnavailableException();
        return "[" + name + "]";
    }
    private static string Literal(string value) => "N'" + value.Replace("'", "''") + "'";
    public string ControlCatalog => new SqlConnectionStringBuilder(storage.Options.ControlConnectionString).InitialCatalog;
    public async Task<bool> ExistsAsync(string catalog, CancellationToken ct = default)
    {
        _ = Identifier(catalog);
        return await Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.databases WHERE name = {catalog}").SingleAsync(ct) == 1;
    }
    [SuppressMessage("Security", "EF1002", Justification = "SQL Server DDL requires identifiers; catalog names pass the strict Identifier allowlist.")]
    public async Task CreateAsync(string catalog, bool requireAbsent = false, CancellationToken ct = default)
    {
        var major = await Database.SqlQueryRaw<int>("SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int) AS [Value]").SingleAsync(ct);
        if (major != 17) throw new InvalidOperationException("SQL Server 2025 (major version 17) is required.");
        if (await ExistsAsync(catalog, ct))
        {
            if (requireAbsent) throw new InvalidOperationException("Catalog already exists; initialization is fresh-deployment-only.");
            return;
        }
        await Database.ExecuteSqlRawAsync($"CREATE DATABASE {Identifier(catalog)}", ct);
        await Database.ExecuteSqlRawAsync($"ALTER DATABASE {Identifier(catalog)} SET COMPATIBILITY_LEVEL = 170", ct);
    }
    private DbContextOptions<T> CatalogOptions<T>(string catalog) where T : DbContext
    {
        _ = Identifier(catalog);
        var builder = new DbContextOptionsBuilder<T>().UseSqlServer(
            new SqlConnectionStringBuilder(storage.Options.AdminConnectionString) { InitialCatalog = catalog }.ConnectionString,
            sql => sql.CommandTimeout(300).UseCompatibilityLevel(170));
        if (storage.LoggerFactory is { } logging) builder.UseLoggerFactory(logging);
        return builder.Options;
    }
    public OrganizationContext OpenOrganization(string key) => new(CatalogOptions<OrganizationContext>(TenantStorage.Catalog(key)));
    public OrganizationContext OpenStaging(string catalog) => new(CatalogOptions<OrganizationContext>(catalog));
    public ControlPlaneContext OpenControl() => new(CatalogOptions<ControlPlaneContext>(ControlCatalog));
    [SuppressMessage("Security", "EF1002", Justification = "Catalog names are allowlisted; configured login names are bracket-escaped and literal-escaped.")]
    public async Task GrantRuntimeAccessAsync(string catalog, string runtimeConnectionString, CancellationToken ct = default)
    {
        var runtime = new SqlConnectionStringBuilder(runtimeConnectionString);
        var administrator = new SqlConnectionStringBuilder(storage.Options.AdminConnectionString);
        if (runtime.IntegratedSecurity && administrator.IntegratedSecurity
            || !runtime.IntegratedSecurity && !administrator.IntegratedSecurity
                && string.Equals(runtime.UserID, administrator.UserID, StringComparison.OrdinalIgnoreCase))
            return; // Local/test same-principal connections already own the catalog. Production rejects them.
        var principal = runtime.UserID;
        if (string.IsNullOrWhiteSpace(principal) || principal.Length > 128)
            throw new InvalidOperationException("The runtime connection must identify an existing server login explicitly.");
        var quoted = "[" + principal.Replace("]", "]]") + "]";
        await Database.ExecuteSqlRawAsync(
            $"USE {Identifier(catalog)}; IF DATABASE_PRINCIPAL_ID({Literal(principal)}) IS NULL CREATE USER {quoted} FOR LOGIN {quoted}; ELSE ALTER USER {quoted} WITH LOGIN = {quoted}; ALTER ROLE [db_datareader] ADD MEMBER {quoted}; ALTER ROLE [db_datawriter] ADD MEMBER {quoted};", ct);
        // USE must not change the administrative boundary's lock/metadata database.
        await Database.ExecuteSqlRawAsync("USE [master]", ct);
    }
    [SuppressMessage("Security", "EF1002", Justification = "BACKUP uses an allowlisted catalog identifier and a single-quote-escaped Literal path.")]
    public async Task BackupAsync(string catalog, string serverPath, CancellationToken ct)
    {
        await Database.ExecuteSqlRawAsync($"BACKUP DATABASE {Identifier(catalog)} TO DISK = {Literal(serverPath)} WITH COPY_ONLY, CHECKSUM, INIT", ct);
        await VerifyBackupAsync(serverPath, ct);
    }
    [SuppressMessage("Security", "EF1002", Justification = "RESTORE requires a literal backup path, escaped by Literal.")]
    public Task VerifyBackupAsync(string serverPath, CancellationToken ct) =>
        Database.ExecuteSqlRawAsync($"RESTORE VERIFYONLY FROM DISK = {Literal(serverPath)} WITH CHECKSUM", ct);
    public async Task<bool> BackupFileExistsAsync(string serverPath, CancellationToken ct) =>
        await Database.SqlQuery<int>($"SELECT CAST(file_exists AS int) AS [Value] FROM sys.dm_os_file_exists({serverPath})").SingleAsync(ct) == 1;
    [SuppressMessage("Security", "EF1002", Justification = "Staging catalog names are allowlisted; backup, logical-file and server paths are escaped by Literal.")]
    public async Task RestoreStagingAsync(string original, string staging, string serverPath, CancellationToken ct)
    {
        var dataPath = await Database.SqlQueryRaw<string>("SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS [Value]").SingleAsync(ct);
        var logPath = await Database.SqlQueryRaw<string>("SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000)) AS [Value]").SingleAsync(ct);
        await Database.ExecuteSqlRawAsync(
            $"RESTORE DATABASE {Identifier(staging)} FROM DISK = {Literal(serverPath)} WITH CHECKSUM, MOVE {Literal(original)} TO {Literal(dataPath + staging + ".mdf")}, MOVE {Literal(original + "_log")} TO {Literal(logPath + staging + "_log.ldf")}", ct);
    }
    [SuppressMessage("Security", "EF1002", Justification = "RESTORE uses an allowlisted catalog identifier and a single-quote-escaped Literal path.")]
    public async Task RestoreAsync(string catalog, string serverPath, CancellationToken ct)
    {
        var exists = await ExistsAsync(catalog, ct);
        if (exists) await Database.ExecuteSqlRawAsync($"ALTER DATABASE {Identifier(catalog)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE", ct);
        try { await Database.ExecuteSqlRawAsync($"RESTORE DATABASE {Identifier(catalog)} FROM DISK = {Literal(serverPath)} WITH REPLACE, CHECKSUM", ct); }
        finally
        {
            if (await ExistsAsync(catalog, CancellationToken.None))
                await Database.ExecuteSqlRawAsync($"ALTER DATABASE {Identifier(catalog)} SET MULTI_USER", CancellationToken.None);
        }
    }
    [SuppressMessage("Security", "EF1002", Justification = "SQL Server DDL requires identifiers; catalog names pass the strict Identifier allowlist.")]
    public async Task DropAsync(string catalog, CancellationToken ct)
    {
        if (!await ExistsAsync(catalog, ct)) return;
        await Database.ExecuteSqlRawAsync($"ALTER DATABASE {Identifier(catalog)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Identifier(catalog)}", ct);
    }
}
