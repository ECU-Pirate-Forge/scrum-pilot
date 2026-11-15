using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.Tenancy;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class OrganizationInvitationConcurrencyTests
{
    [Fact]
    public async Task ActualSqlMembershipPrimaryKey_IsClassifiedPrecisely()
    {
        using var files = new TenantStorageTests(); var org = await files.Create("Unique");
        files.Control.ChangeTracker.Clear();
        files.Control.OrganizationMemberships.Add(new() { OrganizationId = org.OrganizationId, UserId = "owner", Role = OrganizationRole.Owner });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => files.Control.SaveChangesAsync());
        Assert.True(OrganizationInvitationRepositoryExceptionClassifier.IsMembershipUniqueViolation(exception));
        Assert.False(ProjectAccessRepositoryExceptionClassifier.IsProjectMembershipUniqueViolation(exception));
        Assert.False(TransactionConcurrencyExceptionClassifier.IsConcurrencyConflict(exception));
    }

    [Fact]
    public async Task ConcurrentAcceptance_CreatesOnlyOneMembershipInstance()
    {
        using var files = new TenantStorageTests(); var org = await files.Create("InvitationRace");
        var token = new string('A', 64);
        files.Control.OrganizationInvitations.Add(new()
        {
            OrganizationId = org.OrganizationId,
            Email = "member@example.invalid",
            NormalizedEmail = "MEMBER@EXAMPLE.INVALID",
            TokenHash = token,
            InvitedByUserId = "owner",
            Role = OrganizationRole.Member,
            Status = OrganizationInvitationStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await files.Control.SaveChangesAsync();
        async Task<InvitationAcceptanceStatus?> Accept()
        {
            using var context = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>()
                .UseSqlServer(files.Storage.Options.ControlConnectionString).Options);
            try { return (await new OrganizationInvitationRepository(context).AcceptAsync(token, "member", "MEMBER@EXAMPLE.INVALID", DateTime.UtcNow)).Status; }
            catch (OrganizationInvitationConcurrencyException) { return null; }
        }
        var results = await Task.WhenAll(Task.Run(Accept), Task.Run(Accept));
        Assert.Single(results.Where(x => x == InvitationAcceptanceStatus.Success));
        Assert.Single(await files.Control.OrganizationMemberships.AsNoTracking().Where(x => x.UserId == "member").ToListAsync());
    }

    [Fact]
    public async Task ConcurrentLastOwnerRemoval_PreservesAnOwner()
    {
        using var files = new TenantStorageTests(); var org = await files.Create("OwnerRace");
        files.Control.OrganizationMemberships.Add(new() { OrganizationId = org.OrganizationId, UserId = "member", Role = OrganizationRole.Owner });
        await files.Control.SaveChangesAsync();
        async Task Remove(string user)
        {
            using var context = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>()
                .UseSqlServer(files.Storage.Options.ControlConnectionString).Options);
            try { await new OrganizationRepository(context).RemoveMemberAsync(org.OrganizationId, user); }
            catch (Exception ex) when (TransactionConcurrencyExceptionClassifier.IsConcurrencyConflict(ex)) { }
        }
        await Task.WhenAll(Task.Run(() => Remove("owner")), Task.Run(() => Remove("member")));
        Assert.True(await files.Control.OrganizationMemberships.AsNoTracking().AnyAsync(x => x.OrganizationId == org.OrganizationId && x.Role == OrganizationRole.Owner));
    }
}
