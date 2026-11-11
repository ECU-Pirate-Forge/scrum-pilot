using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context;

public sealed class TenantMetadata
{
    public int Id { get; set; } = 1;
    public int OrganizationId { get; set; }
    public required string StorageKey { get; set; }
}

/// <summary>One organization's work. Identity references are scalar, never foreign keys.</summary>
public sealed class OrganizationContext(DbContextOptions<OrganizationContext> options)
    : ScrumPilotContext(options)
{
    internal IDisposable? StorageLease { get; set; }
    public bool HasStorageLease => StorageLease != null;
    public void ReleaseLease() { StorageLease?.Dispose(); StorageLease = null; }
    public void AdoptLease(OrganizationContext source) { StorageLease = source.StorageLease; source.StorageLease = null; }
    public override void Dispose() { base.Dispose(); StorageLease?.Dispose(); StorageLease = null; }
    public override async ValueTask DisposeAsync() { await base.DisposeAsync(); StorageLease?.Dispose(); StorageLease = null; }
    public DbSet<TenantMetadata> TenantMetadata => Set<TenantMetadata>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<Project>().Ignore(x => x.Organization);
        foreach (var type in new[] { typeof(ApplicationUser), typeof(IdentityRole),
            typeof(IdentityUserRole<string>), typeof(IdentityUserClaim<string>),
            typeof(IdentityUserLogin<string>), typeof(IdentityUserToken<string>),
            typeof(IdentityRoleClaim<string>), typeof(Organization),
            typeof(OrganizationMembership), typeof(OrganizationInvitation) })
            model.Ignore(type);
        model.Entity<TenantMetadata>().ToTable("TenantMetadata", t => t.HasCheckConstraint("CK_Metadata_Singleton", "Id = 1"));
        model.Entity<TenantMetadata>().HasKey(x => x.Id);
        model.Entity<TenantMetadata>().Property(x => x.Id).ValueGeneratedNever();
        model.Entity<TenantMetadata>().Property(x => x.StorageKey).IsRequired();
        model.Entity<TenantMetadata>().Property(x => x.StorageKey).HasMaxLength(32);
        model.Entity<ProjectMembership>().Property(x => x.UserId).HasMaxLength(450);
        model.Entity<UserDashboardPreference>().Property(x => x.UserId).HasMaxLength(450);
        model.Entity<ProjectMembership>().Property(x => x.MembershipInstanceId).IsRequired();
    }
}
