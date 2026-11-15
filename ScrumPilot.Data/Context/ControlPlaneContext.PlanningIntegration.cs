using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context;

public sealed partial class ControlPlaneContext
{
    public DbSet<PullRequestScopeAttempt> PullRequestScopeAttempts => Set<PullRequestScopeAttempt>();
    public DbSet<GitHubRepositoryRegistration> GitHubRepositoryRegistrations => Set<GitHubRepositoryRegistration>();
    public DbSet<GitHubWebhookDelivery> GitHubWebhookDeliveries => Set<GitHubWebhookDelivery>();

    private static void ConfigurePlanningIntegration(ModelBuilder model)
    {
        var registration = model.Entity<GitHubRepositoryRegistration>();
        registration.HasKey(item => item.GitHubRepositoryId);
        registration.Property(item => item.GitHubRepositoryId).ValueGeneratedNever();
        registration.HasAlternateKey(item => item.RegistrationId);
        registration.Property(item => item.StorageKey).IsRequired().HasMaxLength(32);
        registration.Property(item => item.Owner).IsRequired().HasMaxLength(100);
        registration.Property(item => item.Name).IsRequired().HasMaxLength(100);
        registration.Property(item => item.RecoveryError).HasMaxLength(2000);
        registration.Property(item => item.Version).IsRowVersion();
        registration.ToTable("GitHubRepositoryRegistrations", table => table.UseSqlOutputClause(false));
        registration.HasIndex(item => new { item.OrganizationId, item.StorageKey, item.ProjectId });
        var attempt = model.Entity<PullRequestScopeAttempt>();
        attempt.HasKey(item => item.PullRequestScopeAttemptId);
        attempt.ToTable("PullRequestScopeAttempts", table => table.UseSqlOutputClause(false));
        attempt.Property(item => item.StorageKey).IsRequired().HasMaxLength(32);
        attempt.Property(item => item.HeadSha).IsRequired().HasMaxLength(64);
        attempt.Property(item => item.HeadSha).UseCollation("Latin1_General_100_BIN2");
        attempt.HasIndex(item => new { item.RegistrationId, item.PullRequestNumber });
        attempt.HasOne<GitHubRepositoryRegistration>().WithMany()
            .HasForeignKey(item => item.RegistrationId).HasPrincipalKey(item => item.RegistrationId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<GitHubWebhookDelivery>().HasKey(item => item.DeliveryId);
        model.Entity<GitHubWebhookDelivery>().Property(item => item.DeliveryId).HasMaxLength(36);
    }

    private void GuardPlanningIntegration()
    {
        if (ChangeTracker.Entries<PullRequestScopeAttempt>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Pull request scope attempts are append-only.");
        if (ChangeTracker.Entries<GitHubRepositoryRegistration>().Any(entry => entry.State == EntityState.Deleted))
            throw new InvalidOperationException("Repository authority is retained for external-check recovery.");
        foreach (var entry in ChangeTracker.Entries<GitHubRepositoryRegistration>().Where(entry => entry.State == EntityState.Modified))
            if (new[] { nameof(GitHubRepositoryRegistration.OrganizationId), nameof(GitHubRepositoryRegistration.StorageKey),
                nameof(GitHubRepositoryRegistration.ProjectId), nameof(GitHubRepositoryRegistration.RegistrationId) }
                .Any(property => entry.Property(property).IsModified))
                throw new InvalidOperationException("Repository authority cannot be reassigned to another organization or project.");
    }
}
