using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

public sealed class RuntimePrincipalTests
{
    [Fact]
    public async Task Provisioning_GrantsOnlyRuntimeDataAccess_WithoutSchemaOrServerPrivileges()
    {
        using var files = new TenantStorageTests();
        using var admin = new SqlServerAdministrationContext(files.Storage);
        var login = "scrumpilot_test_login_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N") + "!aA1";
        var runtime = new SqlConnectionStringBuilder(SqlTestDatabase.Connection)
        {
            IntegratedSecurity = false,
            Authentication = SqlAuthenticationMethod.SqlPassword,
            UserID = login,
            Password = password,
            Pooling = false
        };
        var org = new Organization { Name = "LeastPrivilege", NormalizedName = "LEASTPRIVILEGE" };
        files.Control.Organizations.Add(org); await files.Control.SaveChangesAsync();
        // Password stays parameterized in the outer command and is never logged.
        await admin.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME({login})
                + N' WITH PASSWORD = ' + QUOTENAME({password}, '''') + N', CHECK_POLICY=OFF';
            EXEC (@sql);
            """);
        try
        {
            var configuration = new ConfigurationBuilder().AddConfiguration(files.Database.Configuration)
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:tenant-server"] = runtime.ConnectionString }).Build();
            var storage = new TenantStorage(configuration);
            await new TenantOperations(files.Control, storage).ProvisionAsync(org.OrganizationId);
            await new TenantOperations(files.Control, storage).ProvisionAsync(org.OrganizationId);
            using (var tenant = storage.Open(org))
            {
                tenant.Projects.Add(new() { OrganizationId = org.OrganizationId, ProjectName = "Runtime write" });
                await tenant.SaveChangesAsync();
                Assert.Single(await tenant.Projects.ToListAsync());
                await Assert.ThrowsAsync<SqlException>(() => tenant.Database.ExecuteSqlRawAsync("CREATE TABLE [RuntimeMustNotOwnSchema] ([Id] int NOT NULL)"));
            }
        }
        finally
        {
            // Pooling is disabled, but SQL can still be finishing a client disconnect.
            // Retry this owned login's DROP rather than killing a recyclable session ID.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await admin.Database.ExecuteSqlInterpolatedAsync($"""
                        DECLARE @sql nvarchar(max) = N'DROP LOGIN ' + QUOTENAME({login});
                        EXEC (@sql);
                        """);
                    break;
                }
                catch (SqlException ex) when (ex.Number == 15434 && attempt < 100)
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}
