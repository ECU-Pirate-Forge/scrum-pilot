using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.Tenancy;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class PlanningOrganizationLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SoftDelete_PersistsFenceThenDrainsSharedLeasesBeforeObservers(bool observerFails)
    {
        using var database = new SqlTestDatabase();
        var organization = new Organization
        {
            Name = "Delete",
            NormalizedName = "DELETE",
            CreatedAt = DateTime.UtcNow
        };
        database.Control.Add(organization);
        await database.Control.SaveChangesAsync();
        await database.Operations.ProvisionAsync(organization.OrganizationId);
        var observer = new DrainProbe(database, observerFails);
        var reader = database.Storage.Open(organization);
        var repository = new OrganizationRepository(database.Control, [observer])
        { Storage = database.Storage };
        var deleting = Task.Run(() => repository.SoftDeleteAsync(organization.OrganizationId, DateTime.UtcNow));
        try
        {
            using var independent = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>()
                .UseSqlServer(database.Storage.Options.ControlConnectionString).Options);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!await independent.Organizations.AsNoTracking().AnyAsync(
                       x => x.OrganizationId == organization.OrganizationId && x.DeletedAt != null, timeout.Token))
                await Task.Delay(20, timeout.Token);
            Assert.False(observer.Called);
            Assert.False(deleting.IsCompleted);
        }
        finally { reader.Dispose(); }

        if (observerFails)
            await Assert.ThrowsAsync<InvalidOperationException>(() => deleting.WaitAsync(TimeSpan.FromSeconds(15)));
        else
            Assert.Equal(OrganizationMutationResult.Success, await deleting.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.True(observer.Called);
        Assert.NotNull((await database.Control.Organizations.AsNoTracking().SingleAsync()).DeletedAt);
        Assert.Throws<TenantUnavailableException>(() => database.Storage.Open(organization));
    }

    private sealed class DrainProbe(SqlTestDatabase database, bool fail) : ITenantLifecycleObserver
    {
        public bool Called { get; private set; }
        public async Task OnDrainedAsync(int organizationId, string storageKey, TenantLifecycleOperation operation, CancellationToken ct)
        {
            Assert.Equal(TenantLifecycleOperation.Delete, operation);
            using var control = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>()
                .UseSqlServer(database.Storage.Options.ControlConnectionString).Options);
            Assert.True(await control.Organizations.AnyAsync(
                x => x.OrganizationId == organizationId && x.DeletedAt != null, ct));
            // A separate SQL session cannot obtain even a shared lease while the callback runs.
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(SqlTestDatabase.Connection)
            { InitialCatalog = "master", Pooling = false }.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT APPLOCK_TEST('public', @resource, 'Shared', 'Session')";
            command.Parameters.AddWithValue("@resource", "ScrumPilot:Tenant:" + storageKey);
            Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync(ct)));
            Called = true;
            if (fail) throw new InvalidOperationException("Observer failure leaves durable deletion fenced.");
        }
    }
}
