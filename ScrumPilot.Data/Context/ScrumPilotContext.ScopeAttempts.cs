using Microsoft.EntityFrameworkCore;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context;

public partial class ScrumPilotContext
{
    public DbSet<PullRequestScopeAttempt> PullRequestScopeAttempts => Set<PullRequestScopeAttempt>();

    private static void ConfigureScopeAttempts(ModelBuilder model)
    {
        var attempt = model.Entity<PullRequestScopeAttempt>();
        attempt.HasKey(item => item.PullRequestScopeAttemptId);
        attempt.Property(item => item.HeadSha).IsRequired().HasMaxLength(64);
        attempt.HasIndex(item => new { item.ProjectRepositoryId, item.PullRequestNumber });
        attempt.HasOne<ProjectRepository>().WithMany()
            .HasForeignKey(item => new { item.ProjectId, item.ProjectRepositoryId })
            .HasPrincipalKey(item => new { item.ProjectId, item.ProjectRepositoryId }).OnDelete(DeleteBehavior.Restrict);
    }

    private void PrepareScopeAttempts()
    {
        if (ChangeTracker.Entries<PullRequestScopeAttempt>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Pull request scope attempts are append-only.");
    }
}
