using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ScrumPilot.Data.Tenancy;

/// <summary>Session-owned SQL application locks coordinate CLI jobs and all API processes.</summary>
internal sealed class SqlServerCoordinationContext : DbContext
{
    public SqlServerCoordinationContext(string connectionString, ILoggerFactory? loggerFactory = null)
        : base(CreateOptions(connectionString, loggerFactory)) { }
    private static DbContextOptions<SqlServerCoordinationContext> CreateOptions(string connection, ILoggerFactory? logging)
    {
        var builder = new DbContextOptionsBuilder<SqlServerCoordinationContext>()
            .UseSqlServer(new SqlConnectionStringBuilder(connection) { InitialCatalog = "master", Pooling = false }.ConnectionString,
                sql => sql.CommandTimeout(300).UseCompatibilityLevel(170));
        if (logging != null) builder.UseLoggerFactory(logging);
        return builder.Options;
    }
    public void Acquire(string key, bool exclusive)
    {
        _ = TenantStorage.Catalog(key);
        Database.OpenConnection();
        var resource = "ScrumPilot:Tenant:" + key;
        var mode = exclusive ? "Exclusive" : "Shared";
        Database.ExecuteSqlInterpolated($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode={mode},
                @LockOwner='Session', @LockTimeout=300000, @DbPrincipal='public';
            IF @result < 0 THROW 51000, 'Tenant coordination lock unavailable.', 1;
            """);
    }
}
