using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.ServiceTests;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

public sealed class PlanningTenantIsolationTests
{
    [Fact]
    public async Task GlobalRepositoryAuthorityRejectsAnotherOrganizationWithEqualLocalIds()
    {
        await using var first = await TaskPlanningFixture.CreateAsync();
        await using var second = await TaskPlanningFixture.CreateAsync();
        Assert.Equal(first.ProjectId, second.ProjectId);
        Assert.Equal(first.Repository.ProjectRepositoryId, second.Repository.ProjectRepositoryId);
        // Use the same control directory as the first tenant, not a second isolated installation.
        var otherOrganization = new Organization { Name = "Other", NormalizedName = "OTHER", StorageState = "Active" };
        first.Control.Organizations.Add(otherOrganization);
        await first.Control.SaveChangesAsync();
        var otherRegistry = new GitHubRepositoryRegistry(first.Control,
            new TenantSelection { OrganizationId = otherOrganization.OrganizationId });
        Assert.True(await first.Registry.IsCurrentAsync(first.Repository, default));
        Assert.False(await otherRegistry.IsCurrentAsync(first.Repository, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            otherRegistry.ReserveAsync(second.ProjectId, new(99, 42, "owner", "repository", "main", 0), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => first.Registry.ResolveAsync(99, 43, default));
        var route = await first.Registry.ResolveAsync(99, 42, default);
        Assert.Equal(first.Selection.OrganizationId, route.OrganizationId);
        Assert.Equal(first.Repository.RegistrationId, route.RegistrationId);
    }

    [Fact]
    public async Task CentralRecoveryJournalAndAuthorityCannotBeRewrittenOrDeleted()
    {
        await using var fixture = await TaskPlanningFixture.CreateAsync();
        var attempt = new PullRequestScopeAttempt
        {
            RegistrationId = fixture.Repository.RegistrationId,
            OrganizationId = fixture.Selection.OrganizationId!.Value,
            StorageKey = (await fixture.Control.Organizations.SingleAsync()).StorageKey,
            ProjectId = fixture.ProjectId,
            ProjectRepositoryId = fixture.Repository.ProjectRepositoryId,
            PullRequestNumber = 7,
            HeadSha = TaskPlanningFixture.HeadSha
        };
        fixture.Control.PullRequestScopeAttempts.Add(attempt);
        await fixture.Control.SaveChangesAsync();
        var update = await Assert.ThrowsAsync<SqlException>(() => fixture.Control.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [PullRequestScopeAttempts] SET [HeadSha] = {TaskPlanningFixture.OtherSha}"));
        Assert.Equal(51002, update.Number);
        var delete = await Assert.ThrowsAsync<SqlException>(() => fixture.Control.Database.ExecuteSqlRawAsync(
            "DELETE FROM [PullRequestScopeAttempts]"));
        Assert.Equal(51002, delete.Number);
        var authority = await Assert.ThrowsAsync<SqlException>(() => fixture.Control.Database.ExecuteSqlRawAsync(
            "UPDATE [GitHubRepositoryRegistrations] SET [ProjectId] = [ProjectId] + 1"));
        Assert.Equal(51003, authority.Number);
        Assert.Equal(TaskPlanningFixture.HeadSha, await fixture.Control.PullRequestScopeAttempts.AsNoTracking()
            .Select(item => item.HeadSha).SingleAsync());
        Assert.Null(fixture.Db.Model.FindEntityType(typeof(PullRequestScopeAttempt)));
        Assert.Null(fixture.Db.Model.FindEntityType(typeof(GitHubRepositoryRegistration)));
        Assert.Null(fixture.Control.Model.FindEntityType(typeof(TaskSpecificationRevision)));
    }

    [Fact]
    public async Task AStoredPassingCheckCannotAuthorizeSuspendedOrReconfiguredRepositoryPolicy()
    {
        await using var fixture = await TaskPlanningFixture.CreateAsync();
        await fixture.ApproveAsync();
        var scope = new PullRequestScopeService(fixture.Db, fixture.GitHub, fixture.Specs, fixture.Control, fixture.Registry);
        var check = await scope.BindAsync(fixture.ProjectId, fixture.TaskId, 7, "owner", default);
        Assert.True(await fixture.Registry.IsValidationCurrentAsync(fixture.Repository, check, default));
        var registration = await fixture.Control.GitHubRepositoryRegistrations.SingleAsync();
        registration.State = RepositoryRegistrationState.Suspended;
        await fixture.Control.SaveChangesAsync();
        Assert.False(await fixture.Registry.IsValidationCurrentAsync(fixture.Repository, check, default));
        registration.State = RepositoryRegistrationState.Active;
        fixture.Repository.DefaultBranch = "reviewed-branch";
        await fixture.Db.SaveChangesAsync();
        registration.ConfigurationVersion = fixture.Repository.Version;
        await fixture.Control.SaveChangesAsync();
        Assert.True(await fixture.Registry.IsCurrentAsync(fixture.Repository, default));
        Assert.False(await fixture.Registry.IsValidationCurrentAsync(fixture.Repository, check, default));
    }
}
