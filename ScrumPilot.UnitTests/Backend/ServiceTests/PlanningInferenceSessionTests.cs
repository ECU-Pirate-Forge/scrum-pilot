using System.Data;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class PlanningInferenceSessionTests
{
    [Fact]
    public async Task InferenceClosesConnectionAndReleasesLease_ThenReauthorizesTrustedStorage()
    {
        await using var fixture = await PlanningSqlFixture.CreateAsync();
        var project = await Project(fixture);
        var result = await fixture.Inference.RunAsync(project.ProjectId, () =>
        {
            Assert.False(fixture.Db.HasStorageLease);
            Assert.Equal(ConnectionState.Closed, fixture.Db.Database.GetDbConnection().State);
            Assert.Null(fixture.Db.Database.CurrentTransaction);
            return Task.FromResult("generated");
        }, default);
        Assert.Equal("generated", result);
        Assert.True(fixture.Db.HasStorageLease);
        Assert.Equal(ConnectionState.Open, fixture.Db.Database.GetDbConnection().State);
        Assert.True(await fixture.Access.CanAccessProjectAsync("owner", project.ProjectId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InferenceCannotReenterAfterMembershipRevocationOrReplacement(bool replace)
    {
        await using var fixture = await PlanningSqlFixture.CreateAsync();
        var project = await Project(fixture);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Inference.RunAsync(project.ProjectId, async () =>
        {
            var membership = await fixture.Control.OrganizationMemberships.SingleAsync();
            fixture.Control.Remove(membership);
            await fixture.Control.SaveChangesAsync();
            if (replace)
            {
                fixture.Control.OrganizationMemberships.Add(new()
                {
                    OrganizationId = fixture.Organization.OrganizationId,
                    UserId = "owner",
                    Role = OrganizationRole.Owner
                });
                await fixture.Control.SaveChangesAsync();
            }
            return "must not save";
        }, default));
        Assert.False(fixture.Db.HasStorageLease);
        Assert.Equal(ConnectionState.Closed, fixture.Db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task InferenceCannotReenterAfterStorageLifecycleOperation()
    {
        await using var fixture = await PlanningSqlFixture.CreateAsync();
        var project = await Project(fixture);
        await Assert.ThrowsAsync<TenantUnavailableException>(() => fixture.Inference.RunAsync(project.ProjectId, async () =>
        {
            fixture.Organization.OperationId = Guid.NewGuid();
            await fixture.Control.SaveChangesAsync();
            return "stale";
        }, default));
        Assert.False(fixture.Db.HasStorageLease);
    }

    [Fact]
    public async Task CancelledInferenceStillReacquiresAndVerifiesStorage()
    {
        await using var fixture = await PlanningSqlFixture.CreateAsync();
        var project = await Project(fixture);
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Inference.RunAsync<string>(project.ProjectId, () =>
        {
            cts.Cancel();
            return Task.FromCanceled<string>(cts.Token);
        }, cts.Token));
        Assert.True(fixture.Db.HasStorageLease);
    }

    private static async Task<Project> Project(PlanningSqlFixture fixture)
    {
        var project = new Project { OrganizationId = fixture.Organization.OrganizationId, ProjectName = "Inference" };
        fixture.Db.Projects.Add(project);
        await fixture.Db.SaveChangesAsync();
        return project;
    }
}
