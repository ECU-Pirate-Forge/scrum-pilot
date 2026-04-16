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
        public DbSet<Epic> Epics { get; set; }
        public DbSet<Sprint> Sprints { get; set; }
        public DbSet<Comment> Comments { get; set; }
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
                entity.Property(e => e.EpicId).IsRequired(false);
                entity.Property(e => e.SprintId).IsRequired(false);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Type).HasConversion<string>();
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.Priority).HasConversion<string>();
                entity.Property(e => e.Origin).HasConversion<string>();
                entity.Property(e => e.DateCreated).IsRequired();
                entity.Property(e => e.LastUpdated).IsRequired();
            });

            modelBuilder.Entity<Epic>(entity =>
            {
                entity.ToTable("Epic");
                entity.HasKey(e => e.EpicId);
                entity.Property(e => e.EpicId).ValueGeneratedOnAdd();
                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.DateCreated).IsRequired();
            });

            modelBuilder.Entity<Sprint>(entity =>
            {
                entity.ToTable("Sprint");
                entity.HasKey(e => e.SprintId);
                entity.Property(e => e.SprintId).ValueGeneratedOnAdd();
                entity.Property(e => e.SprintGoal);
                entity.Property(e => e.StartDate);
                entity.Property(e => e.EndDate);
                entity.Property(e => e.IsOpen);
                entity.Property(e => e.DateClosed);
            });

            modelBuilder.Entity<Comment>(entity =>
            {
                entity.ToTable("Comment");
                entity.HasKey(e => e.CommentId);
                entity.Property(e => e.CommentId).ValueGeneratedOnAdd();
                entity.Property(e => e.PbiId).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.Body).IsRequired().HasColumnName("Comment");
                entity.Property(e => e.CreatedDate).IsRequired();
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
                // Use a value converter so Messages serializes as a plain JSON string,
                // which is compatible with both SQLite (TEXT) and Postgres (text).
                entity.Property(e => e.Messages)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<List<DiscordMessage>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<DiscordMessage>()
                    )
                    .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<DiscordMessage>>(
                        (c1, c2) => System.Text.Json.JsonSerializer.Serialize(c1, (System.Text.Json.JsonSerializerOptions?)null) == System.Text.Json.JsonSerializer.Serialize(c2, (System.Text.Json.JsonSerializerOptions?)null),
                        c => c == null ? 0 : System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
                        c => System.Text.Json.JsonSerializer.Deserialize<List<DiscordMessage>>(System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null), (System.Text.Json.JsonSerializerOptions?)null) ?? new List<DiscordMessage>()
                    ));
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