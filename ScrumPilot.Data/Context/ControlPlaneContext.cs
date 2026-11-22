using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context;

/// <summary>Identity and authoritative directory. Never contains project work.</summary>
public sealed partial class ControlPlaneContext(DbContextOptions<ControlPlaneContext> options)
    : ScrumPilotContext(options)
{
    public DbSet<ScrumPilot.Data.Tenancy.TenantBackup> TenantBackups => Set<ScrumPilot.Data.Tenancy.TenantBackup>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<ApplicationUser>().Ignore(x => x.ProjectMemberships);
        model.Entity<Organization>().Ignore(x => x.Projects);
        foreach (var type in new[] { typeof(Project), typeof(ProjectMembership),
            typeof(ProductBacklogItem), typeof(Epic), typeof(Sprint), typeof(Comment),
            typeof(PbiStatusHistory), typeof(UserDashboardPreference) })
            model.Ignore(type);
        // DefaultProjectId is a reference, not a cross-database foreign key.
        model.Entity<Organization>().HasIndex(x => x.StorageKey).IsUnique();
        model.Entity<Organization>().Property(x => x.StorageKey).HasMaxLength(32).IsRequired();
        model.Entity<Organization>().Property(x => x.CatalogName).HasMaxLength(128);
        model.Entity<Organization>().HasIndex(x => x.CatalogName).IsUnique()
            .HasFilter("[CatalogName] IS NOT NULL");
        model.Entity<Organization>().Property(x => x.StorageState).HasMaxLength(32);
        model.Entity<Organization>().Property(x => x.StorageState).IsRequired();
        model.Entity<OrganizationMembership>().Property(x => x.MembershipInstanceId).IsRequired();
        model.Entity<OrganizationMembership>().HasIndex(x => x.MembershipInstanceId).IsUnique();
        model.Entity<ScrumPilot.Data.Tenancy.TenantBackup>().HasKey(x => x.BackupId);
        model.Entity<ScrumPilot.Data.Tenancy.TenantBackup>().Property(x => x.BackupId).HasMaxLength(32);
        model.Entity<ScrumPilot.Data.Tenancy.TenantBackup>().Property(x => x.StorageKey).HasMaxLength(32);
        model.Entity<ScrumPilot.Data.Tenancy.TenantBackup>().Property(x => x.Sha256).HasMaxLength(64);
        model.Entity<ScrumPilot.Data.Tenancy.TenantBackup>().HasIndex(x => new { x.OrganizationId, x.StorageKey });
        ConfigurePlanningIntegration(model);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardMembershipInstances();
        GuardPlanningIntegration();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardMembershipInstances();
        GuardPlanningIntegration();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    private void GuardMembershipInstances()
    {
        foreach (var entry in ChangeTracker.Entries<OrganizationMembership>())
            if (entry.State == EntityState.Modified && entry.Property(x => x.MembershipInstanceId).IsModified)
                throw new InvalidOperationException("Membership instances are immutable.");
    }
}
