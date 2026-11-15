using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.API.Configuration;
using ScrumPilot.Data.Extensions;
using ScrumPilot.Data.Models;
using ScrumPilot.UnitTests.Backend.Tenancy;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class DatabaseSeederBootstrapTests
{
    [Fact]
    public async Task ExplicitInitialize_BootstrapsOnlyConfiguredAdmin_AndCannotReinitialize()
    {
        using var database = new SqlTestDatabase(initialize: false);
        var configuration = new ConfigurationBuilder().AddConfiguration(database.Configuration).AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Email"] = "operator@example.invalid",
            ["BootstrapAdmin:Password"] = "Test-Only-Operator-Password!42"
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddDataServices(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.True(await TenancyCommands.ExecuteAsync(["--initialize"], provider, configuration));
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync("operator@example.invalid");
        Assert.NotNull(user); Assert.True(await users.IsInRoleAsync(user, "Admin"));
        Assert.Equal(1, await database.Control.Users.CountAsync());
        Assert.Empty(await database.Control.Organizations.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => TenancyCommands.ExecuteAsync(["--initialize"], provider, configuration));
    }

    [Fact]
    public async Task NormalStartup_MissingControlDoesNotInitializeOrSeed()
    {
        using var database = new SqlTestDatabase(initialize: false);
        var services = new ServiceCollection().AddLogging();
        services.AddDataServices(database.Configuration);
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => TenancyCommands.ExecuteAsync([], provider, database.Configuration));
        using var admin = new ScrumPilot.Data.Tenancy.SqlServerAdministrationContext(database.Storage);
        Assert.False(await admin.ExistsAsync(database.Catalog));
    }
}
