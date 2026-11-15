using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.Tenancy;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class UserSettingsServiceTests
{
    [Theory]
    [InlineData(OrganizationRole.Owner, false, true)]
    [InlineData(OrganizationRole.Member, false, false)]
    [InlineData(OrganizationRole.Member, true, true)]
    public async Task CurrentDefaults_RequireCurrentMembershipAndInstanceBoundProjectGrant(OrganizationRole role, bool grant, bool expected)
    {
        using var files = new TenantStorageTests();
        var org = await files.Create("Defaults"); var project = await files.Project(org);
        var member = new OrganizationMembership { OrganizationId = org.OrganizationId, UserId = "member", Role = role };
        files.Control.OrganizationMemberships.Add(member); await files.Control.SaveChangesAsync();
        if (grant)
        {
            using var tenant = files.Storage.Open(org);
            tenant.ProjectMemberships.Add(new() { ProjectId = project, UserId = "member", GrantedByUserId = "owner", MembershipInstanceId = member.MembershipInstanceId });
            await tenant.SaveChangesAsync();
        }
        var user = await files.Control.Users.SingleAsync(x => x.Id == "member");
        var service = Service(files, user);
        var result = await service.UpdateSettingsAsync("member", new() { DefaultOrganizationId = org.OrganizationId, DefaultProjectId = project });
        Assert.Equal(expected, result.Succeeded);
        if (expected) Assert.Equal(project, user.DefaultProjectId);
        else Assert.Null(user.DefaultProjectId);
    }

    [Fact]
    public async Task CollidingProjectIds_DoNotAuthorizeForeignOrganizationDefault()
    {
        using var files = new TenantStorageTests();
        var a = await files.Create("DefaultA"); var b = await files.Create("DefaultB");
        var first = await files.Project(a); var second = await files.Project(b); Assert.Equal(first, second);
        var user = await files.Control.Users.SingleAsync(x => x.Id == "member");
        var result = await Service(files, user).UpdateSettingsAsync("member", new() { DefaultOrganizationId = b.OrganizationId, DefaultProjectId = first });
        Assert.False(result.Succeeded); Assert.Null(user.DefaultProjectId);
    }
    [Fact]
    public async Task StaleMembershipGrant_DoesNotSurviveLeaveAndRejoin()
    {
        using var files = new TenantStorageTests(); var org = await files.Create("Rejoin"); var project = await files.Project(org);
        var member = new OrganizationMembership { OrganizationId = org.OrganizationId, UserId = "member", Role = OrganizationRole.Member };
        files.Control.OrganizationMemberships.Add(member); await files.Control.SaveChangesAsync();
        using (var tenant = files.Storage.Open(org))
        {
            tenant.ProjectMemberships.Add(new() { ProjectId = project, UserId = "member", GrantedByUserId = "owner", MembershipInstanceId = member.MembershipInstanceId });
            await tenant.SaveChangesAsync();
        }
        files.Control.OrganizationMemberships.Remove(member); await files.Control.SaveChangesAsync();
        files.Control.OrganizationMemberships.Add(new() { OrganizationId = org.OrganizationId, UserId = "member", Role = OrganizationRole.Member });
        await files.Control.SaveChangesAsync();
        var user = await files.Control.Users.SingleAsync(x => x.Id == "member");
        Assert.False((await Service(files, user).UpdateSettingsAsync("member", new() { DefaultOrganizationId = org.OrganizationId, DefaultProjectId = project })).Succeeded);
    }
    [Fact]
    public async Task InvalidPairOrEmailChange_DoesNotPartiallyMutateProfile()
    {
        using var files = new TenantStorageTests();
        var user = await files.Control.Users.SingleAsync(x => x.Id == "member");
        var service = Service(files, user);
        Assert.False((await service.UpdateSettingsAsync("member", new() { DefaultProjectId = 1, UiPreference = UiPreference.Dark })).Succeeded);
        Assert.Equal(UiPreference.Light, user.UiPreference);
        Assert.False((await service.UpdateSettingsAsync("member", new() { Email = "changed@example.invalid" })).Succeeded);
        Assert.Null(user.Email);
    }
    [Fact]
    public async Task GetDefaults_ClearsDeletedOrUnavailableSelection_WithoutFallback()
    {
        using var files = new TenantStorageTests(); var org = await files.Create("LostDefault", "member");
        var project = await files.Project(org); var user = await files.Control.Users.SingleAsync(x => x.Id == "member");
        user.DefaultOrganizationId = org.OrganizationId; user.DefaultProjectId = project; await files.Control.SaveChangesAsync();
        var service = Service(files, user);
        Assert.Equal(project, (await service.GetSettingsAsync("member"))!.DefaultProjectId);
        org.DeletedAt = DateTime.UtcNow; await files.Control.SaveChangesAsync();
        var settings = await service.GetSettingsAsync("member");
        Assert.Null(settings!.DefaultOrganizationId); Assert.Null(settings.DefaultProjectId);
    }
    [Fact]
    public async Task ClearingDefaults_PreservesIdentitySecurityFields()
    {
        using var files = new TenantStorageTests(); var user = await files.Control.Users.SingleAsync(x => x.Id == "member");
        var stamp = user.SecurityStamp; var normalized = user.NormalizedUserName;
        Assert.True((await Service(files, user).UpdateSettingsAsync("member", new() { UiPreference = UiPreference.Dark })).Succeeded);
        Assert.Equal(stamp, user.SecurityStamp); Assert.Equal(normalized, user.NormalizedUserName); Assert.Equal(UiPreference.Dark, user.UiPreference);
    }
    private static UserSettingsService Service(TenantStorageTests files, ApplicationUser user)
    {
        var manager = Substitute.For<UserManager<ApplicationUser>>(Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        manager.FindByIdAsync(user.Id).Returns(user);
        return new(manager, files.Control) { Storage = files.Storage };
    }
}
