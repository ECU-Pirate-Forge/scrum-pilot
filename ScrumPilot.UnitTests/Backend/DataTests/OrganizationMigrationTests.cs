using Microsoft.EntityFrameworkCore;
using ScrumPilot.UnitTests.Backend.Tenancy;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class OrganizationMigrationTests
{
    [Fact]
    public async Task FreshIndependentMigrations_DoNotSeedProductionIdentitiesOrOrganizations()
    {
        using var database = new SqlTestDatabase();
        Assert.Empty(await database.Control.Users.ToListAsync());
        Assert.Empty(await database.Control.Organizations.ToListAsync());
        Assert.Single(await database.Control.Database.GetAppliedMigrationsAsync());
        Assert.False(database.Control.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task OrganizationMigrations_AreIndependentFromCentralIdentity()
    {
        using var files = new TenantStorageTests();
        var org = await files.Create("Migrated");
        using var tenant = files.Storage.Open(org);
        Assert.Single(await tenant.Database.GetAppliedMigrationsAsync());
        Assert.False(tenant.Database.HasPendingModelChanges());
        Assert.Empty(await tenant.Projects.ToListAsync());
        Assert.Equal(org.StorageKey, (await tenant.TenantMetadata.SingleAsync()).StorageKey);
    }
}
