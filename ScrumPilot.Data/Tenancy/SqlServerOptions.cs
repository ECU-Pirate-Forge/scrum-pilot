using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace ScrumPilot.Data.Tenancy;

public sealed class SqlServerOptions
{
    public const int CompatibilityLevel = 170;
    public required string ControlConnectionString { get; init; }
    public required string RuntimeConnectionString { get; init; }
    public required string AdminConnectionString { get; init; }
    public required string BackupServerPath { get; init; }
    public required string BackupApplicationPath { get; init; }

    public static SqlServerOptions FromConfiguration(IConfiguration configuration, string? environmentName = null)
    {
        string Required(string name) => configuration.GetConnectionString(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"ConnectionStrings:{name} is required.");
        var control = Required("control");
        var runtime = Required("tenant-server");
        var admin = Required("tenant-admin");
        var production = string.Equals(environmentName ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"] ?? "Production", "Production", StringComparison.OrdinalIgnoreCase);
        foreach (var value in new[] { control, runtime, admin })
        {
            var parsed = new SqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(parsed.DataSource))
                throw new InvalidOperationException("SQL Server DataSource is required.");
            if (production && (parsed.TrustServerCertificate || parsed.Encrypt == SqlConnectionEncryptOption.Optional))
                throw new InvalidOperationException("Production SQL connections require encryption and validated TLS certificates.");
        }
        if (production)
        {
            var administrator = new SqlConnectionStringBuilder(admin);
            foreach (var connection in new[] { control, runtime })
            {
                var ordinary = new SqlConnectionStringBuilder(connection);
                var samePrincipal = ordinary.IntegratedSecurity && administrator.IntegratedSecurity
                    || !ordinary.IntegratedSecurity && !administrator.IntegratedSecurity
                    && ordinary.Authentication == administrator.Authentication
                    && string.Equals(ordinary.UserID, administrator.UserID, StringComparison.OrdinalIgnoreCase);
                if (string.Equals(ordinary.UserID, "sa", StringComparison.OrdinalIgnoreCase) || samePrincipal)
                    throw new InvalidOperationException("Production runtime SQL principals must be non-SA and distinct from the privileged tenant-admin principal.");
            }
        }
        if (string.IsNullOrWhiteSpace(new SqlConnectionStringBuilder(control).InitialCatalog))
            throw new InvalidOperationException("The control catalog must be explicit.");
        var serverPath = configuration["SqlServer:BackupServerPath"] ?? "/var/opt/mssql/backup";
        var applicationPath = configuration["SqlServer:BackupApplicationPath"] ?? Path.Combine(AppContext.BaseDirectory, "backups");
        if (!Path.IsPathFullyQualified(applicationPath))
            throw new InvalidOperationException("SqlServer:BackupApplicationPath must be absolute.");
        if (serverPath.Contains('\'') || serverPath.Contains('\r') || serverPath.Contains('\n'))
            throw new InvalidOperationException("Invalid SQL backup server path.");
        return new()
        {
            ControlConnectionString = control,
            RuntimeConnectionString = runtime,
            AdminConnectionString = admin,
            BackupServerPath = serverPath.TrimEnd('/', '\\'),
            BackupApplicationPath = Path.GetFullPath(applicationPath)
        };
    }
}
