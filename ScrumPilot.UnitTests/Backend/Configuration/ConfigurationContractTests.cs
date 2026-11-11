using Microsoft.Extensions.Configuration;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.API.Configuration;

namespace ScrumPilot.UnitTests.Backend.Configuration;

public sealed class ConfigurationContractTests
{
    [Theory]
    [InlineData(null, "http://localhost:11434/", "gemma4:latest")]
    [InlineData("http://localhost:11434", "http://localhost:11434/", "gemma4:latest")]
    [InlineData("Endpoint=http://localhost:18000;Model=custom:latest", "http://localhost:18000/", "custom:latest")]
    public void OllamaAcceptsAspireConnectionStringsAndDirectStartupFallback(string? connection, string endpoint, string model)
    {
        var options = AiOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:ollama"] = connection }).Build());
        Assert.Equal(endpoint, options.Endpoint.AbsoluteUri); Assert.Equal(model, options.Model);
        var normalized = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = options.ConnectionString };
        Assert.Equal(endpoint, normalized["Endpoint"]); Assert.Equal(model, normalized["Model"]);
    }

    [Theory]
    [InlineData("Endpoint=file:///tmp/ollama;Model=test")]
    [InlineData("Endpoint=http://user:password@localhost;Model=test")]
    [InlineData("Model=only-model")]
    public void OllamaRejectsInvalidEndpoints(string connection) =>
        Assert.Throws<InvalidOperationException>(() => AiOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:ollama"] = connection }).Build()));

    [Fact]
    public void RuntimeCatalogIsAlwaysDerivedFromOpaqueRegistryKey()
    {
        var storage = new TenantStorage(Configuration());
        var key = Guid.NewGuid().ToString("N");
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(storage.OrganizationConnectionString(key));
        Assert.Equal("scrumpilot_org_" + key, connection.InitialCatalog);
        Assert.NotEqual("untrusted_template_catalog", connection.InitialCatalog);
    }

    [Theory]
    [InlineData("../master")]
    [InlineData("master")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void RegistryRejectsNonOpaqueKeys(string key)
    {
        var storage = new TenantStorage(Configuration());
        Assert.Throws<TenantUnavailableException>(() => storage.OrganizationConnectionString(key));
    }

    [Theory]
    [InlineData("control")]
    [InlineData("tenant-server")]
    [InlineData("tenant-admin")]
    public void ProductionRejectsUnvalidatedSqlTls(string connectionName)
    {
        var values = new Dictionary<string, string?> { ["DOTNET_ENVIRONMENT"] = "Production" };
        foreach (var name in new[] { "control", "tenant-server", "tenant-admin" })
            values["ConnectionStrings:" + name] = $"Server=localhost;Database=control;User ID={name};Encrypt=true;TrustServerCertificate={(name == connectionName ? "true" : "false")}";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<InvalidOperationException>(() => SqlServerOptions.FromConfiguration(configuration));
    }

    [Theory]
    [InlineData("sa", "privileged")]
    [InlineData("privileged", "privileged")]
    public void ProductionRejectsPrivilegedRuntimePrincipal(string runtimeUser, string adminUser)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ConnectionStrings:control"] = $"Server=localhost;Database=control;User ID={runtimeUser};Encrypt=true;TrustServerCertificate=false",
            ["ConnectionStrings:tenant-server"] = $"Server=localhost;User ID={runtimeUser};Encrypt=true;TrustServerCertificate=false",
            ["ConnectionStrings:tenant-admin"] = $"Server=localhost;User ID={adminUser};Encrypt=true;TrustServerCertificate=false"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => SqlServerOptions.FromConfiguration(configuration));
    }

    internal static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Testing",
            ["ConnectionStrings:control"] = "Server=localhost;Database=scrumpilot_control;Integrated Security=true;Encrypt=true;TrustServerCertificate=true",
            ["ConnectionStrings:tenant-server"] = "Server=localhost;Database=untrusted_template_catalog;Integrated Security=true;Encrypt=true;TrustServerCertificate=true",
            ["ConnectionStrings:tenant-admin"] = "Server=localhost;Database=master;Integrated Security=true;Encrypt=true;TrustServerCertificate=true",
            ["SqlServer:BackupServerPath"] = "/var/opt/mssql/backup",
            ["SqlServer:BackupApplicationPath"] = Path.GetTempPath()
        }).Build();
}
