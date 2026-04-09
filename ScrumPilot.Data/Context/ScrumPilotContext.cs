using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context
{
    public class ScrumPilotContext : IdentityDbContext<ApplicationUser>
    {
        public ScrumPilotContext(DbContextOptions<ScrumPilotContext> options) : base(options)
        {
        }

        public DbSet<ProductBacklogItem> Stories { get; set; }
        public DbSet<AudioTranscript> AudioTranscripts { get; set; }
        public DbSet<MessageTranscript> MessageTranscripts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure ApplicationUser entity
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(e => e.UiPreference).HasConversion<string>().HasDefaultValue(UiPreference.Light);
            });

            // Configure ProductBacklogItem entity
            modelBuilder.Entity<ProductBacklogItem>(entity =>
            {
                entity.HasKey(e => e.PbiId);
                entity.Property(e => e.PbiId).ValueGeneratedOnAdd();
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Type).HasConversion<string>();
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.Priority).HasConversion<string>();
                entity.Property(e => e.Origin).HasConversion<string>();
                entity.Property(e => e.DateCreated).IsRequired();
                entity.Property(e => e.LastUpdated).IsRequired();
            });

            modelBuilder.Entity<AudioTranscript>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.Transcript).IsRequired();
                entity.Property(e => e.RecordedAt).IsRequired();
            });

            modelBuilder.Entity<MessageTranscript>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.OwnsMany(e => e.Messages, messages =>
                {
                    messages.ToJson();
                    messages.OwnsOne(m => m.Author);
                });
            });
        }

        public override int SaveChanges()
        {
            UpdateTimestamps();
            return base.SaveChanges();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            UpdateTimestamps();
            return await base.SaveChangesAsync(cancellationToken);
        }

        private void UpdateTimestamps()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                if (entry.Entity is ProductBacklogItem story)
                {
                    if (entry.State == EntityState.Added)
                    {
                        story.DateCreated = DateTime.UtcNow;
                    }
                    story.LastUpdated = DateTime.UtcNow;
                }
            }
        }
    }
}