using Microsoft.EntityFrameworkCore;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context
{
    public class ScrumPilotContext : DbContext
    {
        public ScrumPilotContext(DbContextOptions<ScrumPilotContext> options) : base(options)
        {
        }

        public DbSet<Story> Stories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Story entity
            modelBuilder.Entity<Story>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd(); // Auto-increment identity column
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.Priority).HasConversion<string>();
                entity.Property(e => e.Origin).HasConversion<string>();
                entity.Property(e => e.DateCreated).IsRequired();
                entity.Property(e => e.LastUpdated).IsRequired();
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
                if (entry.Entity is Story story)
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