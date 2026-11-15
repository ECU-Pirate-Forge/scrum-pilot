using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

internal sealed class TenantAccessFixture : IDisposable
{
    public TenantStorageTests Files { get; } = new();
    public Organization Organization { get; }
    public OrganizationContext Tenant { get; }
    public TenantProjectAccessRepository Repository { get; }
    public TenantOrganizationAccessService Access { get; }
    public OrganizationMembership Member { get; }
    public int ProjectId { get; }
    private readonly ServiceProvider services;
    public TenantAccessFixture(bool grant = true)
    {
        Organization = Files.Create("Access").GetAwaiter().GetResult();
        ProjectId = Files.Project(Organization).GetAwaiter().GetResult();
        Member = new() { OrganizationId = Organization.OrganizationId, UserId = "member", Role = OrganizationRole.Member };
        Files.Control.OrganizationMemberships.Add(Member); Files.Control.SaveChanges();
        Tenant = Files.Storage.Open(Organization);
        if (grant)
        {
            Tenant.ProjectMemberships.Add(new() { ProjectId = ProjectId, UserId = "member", GrantedByUserId = "owner", MembershipInstanceId = Member.MembershipInstanceId });
            Tenant.SaveChanges();
        }
        var selection = new TenantSelection { OrganizationId = Organization.OrganizationId };
        Repository = new(Tenant, Files.Control, selection);
        services = new ServiceCollection().AddSingleton(Tenant).AddSingleton<IProjectAccessRepository>(Repository).BuildServiceProvider();
        Access = new(Files.Control, services, selection);
    }
    public void Dispose() { services.Dispose(); Tenant.Dispose(); Files.Dispose(); }
}
