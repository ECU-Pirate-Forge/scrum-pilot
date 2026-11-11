using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

/// <summary>Real SQL Server 2025, one owned catalog per fixture. Missing configuration fails, never skips.</summary>
internal sealed class SqlTestDatabase : IDisposable
{
    public string Catalog { get; } = "scrumpilot_test_" + Guid.NewGuid().ToString("N");
    public IConfiguration Configuration { get; }
    public TenantStorage Storage { get; }
    public ControlPlaneContext Control { get; }
    public TenantOperations Operations { get; }
    public static string Connection => Environment.GetEnvironmentVariable("SCRUMPILOT_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("SCRUMPILOT_TEST_SQL_CONNECTION must point to the dedicated owned SQL Server 2025 test resource.");
    public SqlTestDatabase(bool initialize = true)
    {
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Testing",
            ["ConnectionStrings:control"] = new SqlConnectionStringBuilder(Connection) { InitialCatalog = Catalog }.ConnectionString,
            ["ConnectionStrings:tenant-server"] = new SqlConnectionStringBuilder(Connection) { InitialCatalog = "must_be_replaced" }.ConnectionString,
            ["ConnectionStrings:tenant-admin"] = new SqlConnectionStringBuilder(Connection) { InitialCatalog = "master" }.ConnectionString,
            ["ConnectionStrings:ollama"] = "http://localhost:11434",
            ["SqlServer:BackupServerPath"] = Environment.GetEnvironmentVariable("SCRUMPILOT_TEST_SQL_BACKUP_SERVER_PATH")
                ?? throw new InvalidOperationException("SQL backup server mapping is required."),
            ["SqlServer:BackupApplicationPath"] = Environment.GetEnvironmentVariable("SCRUMPILOT_TEST_SQL_BACKUP_APPLICATION_PATH")
                ?? throw new InvalidOperationException("SQL backup application mapping is required.")
        }).Build();
        Storage = new(Configuration);
        Control = new(new DbContextOptionsBuilder<ControlPlaneContext>()
            .UseSqlServer(Configuration.GetConnectionString("control"), sql => sql.UseCompatibilityLevel(170)).Options);
        Operations = new(Control, Storage);
        if (initialize)
        {
            using var admin = new SqlServerAdministrationContext(Storage);
            admin.CreateAsync(Catalog, requireAbsent: true).GetAwaiter().GetResult();
            Control.Database.Migrate();
            TenantStorage.VerifyServer(Control);
        }
    }

    public void Dispose()
    {
        using var admin = new SqlServerAdministrationContext(Storage);
        if (admin.ExistsAsync(Catalog).GetAwaiter().GetResult())
        {
            foreach (var key in Control.Organizations.AsNoTracking().Select(x => x.StorageKey).ToList())
            {
                admin.DropAsync(TenantStorage.Catalog(key), default).GetAwaiter().GetResult();
                foreach (var file in Directory.EnumerateFiles(Storage.Options.BackupApplicationPath, key + "_*.bak"))
                    File.Delete(Storage.Checked(file));
            }
            Control.Dispose();
            if (!Catalog.StartsWith("scrumpilot_test_", StringComparison.Ordinal)) throw new InvalidOperationException("Not an owned test catalog.");
            admin.DropAsync(Catalog, default).GetAwaiter().GetResult();
        }
        else Control.Dispose();
    }
}
