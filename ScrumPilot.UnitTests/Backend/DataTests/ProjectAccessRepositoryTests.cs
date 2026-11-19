using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.Tenancy;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class ProjectAccessRepositoryTests
{
    [Fact]
    public async Task Grants_AreIdempotent_AndRevocationClearsOnlyQualifiedDefault()
    {
        using var fixture = new TenantAccessFixture(grant: false);
        Assert.Equal(ProjectAccessMutationResult.Success, await fixture.Repository.SetAccessAsync(fixture.ProjectId, "member", true, "owner", DateTime.UtcNow));
        Assert.Equal(ProjectAccessMutationResult.Success, await fixture.Repository.SetAccessAsync(fixture.ProjectId, "member", true, "owner", DateTime.UtcNow));
        Assert.Single(await fixture.Tenant.ProjectMemberships.ToListAsync());
        var user = await fixture.Files.Control.Users.SingleAsync(x => x.Id == "member");
        user.DefaultOrganizationId = fixture.Organization.OrganizationId; user.DefaultProjectId = fixture.ProjectId;
        await fixture.Files.Control.SaveChangesAsync();
        await fixture.Repository.SetAccessAsync(fixture.ProjectId, "member", false, "owner", DateTime.UtcNow);
        await fixture.Files.Control.Entry(user).ReloadAsync(); Assert.Null(user.DefaultProjectId);
        Assert.Empty(await fixture.Tenant.ProjectMemberships.ToListAsync());
    }
    [Fact]
    public async Task Members_IncludeImplicitOwnerAndOnlyCurrentExplicitMembers()
    {
        using var fixture = new TenantAccessFixture();
        var members = await fixture.Repository.GetMembersAsync(fixture.ProjectId);
        Assert.Equal(2, members.Count); Assert.Contains(members, x => x.UserId == "owner");
        fixture.Files.Control.OrganizationMemberships.Remove(fixture.Member); await fixture.Files.Control.SaveChangesAsync();
        Assert.Single(await fixture.Repository.GetMembersAsync(fixture.ProjectId));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UngrantedMember_IsVisibleOnlyInGrantManagementList(bool includeUngranted)
    {
        using var fixture = new TenantAccessFixture(grant: false);
        var user = await fixture.Files.Control.Users.SingleAsync(x => x.Id == "member");
        user.UserName = "new-member@example.test";
        await fixture.Files.Control.SaveChangesAsync();

        var members = await fixture.Repository.GetMembersAsync(fixture.ProjectId, includeUngranted: includeUngranted);
        Assert.Contains(members, x => x.UserId == "owner");
        if (includeUngranted)
        {
            var member = Assert.Single(members, x => x.UserId == "member");
            Assert.False(member.HasExplicitAccess);
            Assert.Equal("new-member@example.test", member.UserName);
        }
        else
        {
            Assert.DoesNotContain(members, x => x.UserId == "member");
        }
    }

    [Theory]
    [InlineData("owner", ProjectAccessMutationResult.TargetIsOwner)]
    [InlineData("admin", ProjectAccessMutationResult.TargetNotMember)]
    public async Task Mutation_RequiresOrdinaryCurrentTarget(string user, ProjectAccessMutationResult expected)
    {
        using var fixture = new TenantAccessFixture();
        Assert.Equal(expected, await fixture.Repository.SetAccessAsync(fixture.ProjectId, user, true, "owner", DateTime.UtcNow));
    }
    [Fact]
    public async Task ActualSqlPrimaryKeyViolation_IsClassifiedWithoutMaskingOtherFailures()
    {
        using var fixture = new TenantAccessFixture();
        fixture.Tenant.ChangeTracker.Clear();
        fixture.Tenant.ProjectMemberships.Add(new() { ProjectId = fixture.ProjectId, UserId = "member", GrantedByUserId = "owner", MembershipInstanceId = fixture.Member.MembershipInstanceId });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Tenant.SaveChangesAsync());
        Assert.True(ProjectAccessRepositoryExceptionClassifier.IsProjectMembershipUniqueViolation(exception));
        Assert.False(OrganizationInvitationRepositoryExceptionClassifier.IsMembershipUniqueViolation(exception));
    }
    [Fact]
    public async Task RemovedMember_CannotReceiveGrant()
    {
        using var fixture = new TenantAccessFixture();
        fixture.Files.Control.OrganizationMemberships.Remove(fixture.Member); await fixture.Files.Control.SaveChangesAsync();
        Assert.Equal(ProjectAccessMutationResult.TargetNotMember, await fixture.Repository.SetAccessAsync(fixture.ProjectId, "member", true, "owner", DateTime.UtcNow));
    }
}
