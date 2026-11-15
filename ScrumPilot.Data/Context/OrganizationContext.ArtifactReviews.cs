using Microsoft.EntityFrameworkCore;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context;

public sealed partial class OrganizationContext
{
    public DbSet<PlanningArtifactReview> PlanningArtifactReviews => Set<PlanningArtifactReview>();

    private static void ConfigurePlanningArtifactReviews(ModelBuilder model)
    {
        var review = model.Entity<PlanningArtifactReview>();
        review.ToTable("PlanningArtifactReviews");
        review.HasKey(r => r.PlanningArtifactReviewId);
        review.Property(r => r.PlanningArtifactReviewId).ValueGeneratedOnAdd();
        review.Property(r => r.Fingerprint).IsRequired().HasMaxLength(64);
        review.Property(r => r.ActorId).IsRequired();
        review.Property(r => r.Reason).IsRequired().HasMaxLength(2000);
        review.HasIndex(r => new { r.ProjectId, r.PbiId, r.Fingerprint });
        review.HasOne<Project>().WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        review.HasOne<ProductBacklogItem>().WithMany().HasForeignKey(r => new { r.ProjectId, r.PbiId })
            .HasPrincipalKey(p => new { p.ProjectId, p.PbiId }).OnDelete(DeleteBehavior.Restrict);
    }

    private void PreparePlanningArtifactReviews()
    {
        if (ChangeTracker.Entries<PlanningArtifactReview>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("PlanningArtifactReview is append-only.");
    }
}
