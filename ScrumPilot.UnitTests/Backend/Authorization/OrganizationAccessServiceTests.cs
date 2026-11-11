using Microsoft.EntityFrameworkCore;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.Tenancy;

namespace ScrumPilot.UnitTests.Backend.Authorization;

public sealed class OrganizationAccessServiceTests
{
    [Theory]
    [InlineData("owner", true)]
    [InlineData("member", true)]
    [InlineData("admin", false)]
    [InlineData("unknown", false)]
    public async Task CurrentMembership_NotGlobalRole_IsRequired(string user, bool allowed)
    {
        using var fixture = new TenantAccessFixture();
        Assert.Equal(allowed, await fixture.Access.CanAccessProjectAsync(user, fixture.ProjectId));
        Assert.Equal(user == "owner", await fixture.Access.IsOrganizationOwnerAsync(user, fixture.Organization.OrganizationId));
    }
    [Fact]
    public async Task MemberNeedsExplicitGrant()
    {
        using var fixture = new TenantAccessFixture(grant: false);
        Assert.False(await fixture.Access.CanAccessProjectAsync("member", fixture.ProjectId));
        Assert.True(await fixture.Access.CanAccessProjectAsync("owner", fixture.ProjectId));
    }
    [Fact]
    public async Task LeaveAndRejoin_InvalidatesOldMembershipInstance()
    {
        using var fixture = new TenantAccessFixture();
        fixture.Files.Control.OrganizationMemberships.Remove(fixture.Member); await fixture.Files.Control.SaveChangesAsync();
        fixture.Files.Control.OrganizationMemberships.Add(new() { OrganizationId = fixture.Organization.OrganizationId, UserId = "member", Role = OrganizationRole.Member });
        await fixture.Files.Control.SaveChangesAsync();
        Assert.False(await fixture.Access.CanAccessProjectAsync("member", fixture.ProjectId));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletedOrMaintenanceOrganization_DeniesWork(bool deleted)
    {
        using var fixture = new TenantAccessFixture();
        if (deleted) fixture.Organization.DeletedAt = DateTime.UtcNow; else fixture.Organization.StorageState = "Maintenance";
        await fixture.Files.Control.SaveChangesAsync();
        Assert.False(await fixture.Access.CanAccessProjectAsync("owner", fixture.ProjectId));
    }
    [Fact]
    public async Task HierarchyQueries_UseCurrentCatalogAndPersistedParent()
    {
        using var fixture = new TenantAccessFixture();
        var sprint = new Sprint { ProjectId = fixture.ProjectId, SprintTitle = "Sprint" };
        var epic = new Epic { ProjectId = fixture.ProjectId, Name = "Epic" };
        fixture.Tenant.Sprints.Add(sprint); fixture.Tenant.Epics.Add(epic); await fixture.Tenant.SaveChangesAsync();
        var pbi = new ProductBacklogItem { ProjectId = fixture.ProjectId, Title = "Story", SprintId = sprint.SprintId, EpicId = epic.EpicId };
        fixture.Tenant.Stories.Add(pbi); await fixture.Tenant.SaveChangesAsync();
        Assert.Equal(fixture.Organization.OrganizationId, await fixture.Access.GetOrganizationIdForPbiAsync(pbi.PbiId));
        Assert.Equal(fixture.Organization.OrganizationId, await fixture.Access.GetOrganizationIdForSprintAsync(sprint.SprintId));
        Assert.Equal(fixture.Organization.OrganizationId, await fixture.Access.GetOrganizationIdForEpicAsync(epic.EpicId));
        Assert.True(await fixture.Access.SprintBelongsToProjectAsync(sprint.SprintId, fixture.ProjectId));
        Assert.False(await fixture.Access.SprintBelongsToProjectAsync(sprint.SprintId, 999));
        Assert.False(await fixture.Access.EpicBelongsToProjectAsync(epic.EpicId, 999));
        Assert.False(await fixture.Access.PbiBelongsToProjectAsync(pbi.PbiId, 999));
        Assert.Null(await fixture.Access.GetOrganizationIdForPbiAsync(999));
    }
    [Fact]
    public async Task AssigneeValidation_UsesCurrentAccess()
    {
        using var fixture = new TenantAccessFixture();
        Assert.True(await fixture.Access.UserCanBeAssignedToProjectAsync("member", fixture.ProjectId));
        await fixture.Tenant.ProjectMemberships.ExecuteDeleteAsync();
        Assert.False(await fixture.Access.UserCanBeAssignedToProjectAsync("member", fixture.ProjectId));
        Assert.False(await fixture.Access.UserCanBeAssignedToProjectAsync("admin", fixture.ProjectId));
    }
    [Fact]
    public async Task Cancellation_PropagatesToSqlQueries()
    {
        using var fixture = new TenantAccessFixture(); using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Access.CanAccessProjectAsync("owner", fixture.ProjectId, ct.Token));
    }
}
