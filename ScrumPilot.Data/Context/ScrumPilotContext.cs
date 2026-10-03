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

        public DbSet<Project> Projects { get; set; }
        public DbSet<ProductBacklogItem> Stories { get; set; }
        public DbSet<Epic> Epics { get; set; }
        public DbSet<Sprint> Sprints { get; set; }
        public DbSet<Comment> Comments { get; set; }
        public DbSet<AudioTranscript> AudioTranscripts { get; set; }
        public DbSet<MessageTranscript> MessageTranscripts { get; set; }
        public DbSet<PbiStatusHistory> PbiStatusHistories { get; set; }
        public DbSet<UserDashboardPreference> UserDashboardPreferences { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<OrganizationMembership> OrganizationMemberships { get; set; }
        public DbSet<ProjectMembership> ProjectMemberships { get; set; }
        public DbSet<OrganizationInvitation> OrganizationInvitations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure ApplicationUser entity
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(e => e.UiPreference).HasConversion<string>().HasDefaultValue(UiPreference.Light);
                entity.Property(e => e.DefaultOrganizationId).IsRequired(false);
                entity.Property(e => e.DefaultProjectId).IsRequired(false);
                entity.HasOne<Organization>()
                    .WithMany()
                    .HasForeignKey(e => e.DefaultOrganizationId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(e => e.DefaultProjectId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Organization>(entity =>
            {
                entity.ToTable("Organizations");
                entity.HasKey(e => e.OrganizationId);
                entity.Property(e => e.OrganizationId).ValueGeneratedOnAdd();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.NormalizedName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.DeletedAt).IsRequired(false);
                entity.Property(e => e.RowVersion).IsRequired().IsConcurrencyToken();
                entity.HasIndex(e => e.NormalizedName).IsUnique();
            });

            modelBuilder.Entity<OrganizationMembership>(entity =>
            {
                entity.ToTable("OrganizationMemberships");
                entity.HasKey(e => new { e.OrganizationId, e.UserId });
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.Role).HasConversion<string>().IsRequired();
                entity.Property(e => e.JoinedAt).IsRequired();
                entity.HasOne(e => e.Organization)
                    .WithMany(e => e.OrganizationMemberships)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<ApplicationUser>()
                    .WithMany(e => e.OrganizationMemberships)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ProjectMembership>(entity =>
            {
                entity.ToTable("ProjectMemberships");
                entity.HasKey(e => new { e.ProjectId, e.UserId });
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.GrantedAt).IsRequired();
                entity.Property(e => e.GrantedByUserId).IsRequired();
                entity.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<ApplicationUser>()
                    .WithMany(e => e.ProjectMemberships)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<ApplicationUser>()
                    .WithMany()
                    .HasForeignKey(e => e.GrantedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<OrganizationInvitation>(entity =>
            {
                entity.ToTable("OrganizationInvitations");
                entity.HasKey(e => e.OrganizationInvitationId);
                entity.Property(e => e.OrganizationInvitationId).ValueGeneratedOnAdd();
                entity.Property(e => e.Email).IsRequired().HasMaxLength(320);
                entity.Property(e => e.NormalizedEmail).IsRequired().HasMaxLength(320);
                entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
                entity.Property(e => e.InvitedByUserId).IsRequired();
                entity.Property(e => e.Role).HasConversion<string>().IsRequired();
                entity.Property(e => e.Status).HasConversion<string>().IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.ExpiresAt).IsRequired();
                entity.Property(e => e.AcceptedAt).IsRequired(false);
                entity.Property(e => e.LastSentAt).IsRequired(false);
                entity.Property(e => e.DeliveryError).IsRequired(false);
                entity.HasOne<Organization>()
                    .WithMany()
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<ApplicationUser>()
                    .WithMany()
                    .HasForeignKey(e => e.InvitedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(e => new { e.OrganizationId, e.NormalizedEmail, e.Status });
                entity.HasIndex(e => e.TokenHash).IsUnique();
                entity.HasIndex(e => e.ExpiresAt);
            });

            modelBuilder.Entity<Project>(entity =>
            {
                entity.ToTable("Project");
                entity.HasKey(e => e.ProjectId);
                entity.Property(e => e.ProjectId).ValueGeneratedOnAdd();
                entity.Property(e => e.ProjectName).IsRequired();
                entity.Property(e => e.OrganizationId).IsRequired();
                entity.HasIndex(e => e.OrganizationId);
                entity.HasOne(e => e.Organization)
                    .WithMany(e => e.Projects)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Configure ProductBacklogItem entity
            modelBuilder.Entity<ProductBacklogItem>(entity =>
            {
                entity.HasKey(e => e.PbiId);
                entity.Property(e => e.PbiId).ValueGeneratedOnAdd();
                entity.Property(e => e.ProjectId).IsRequired();
                entity.Property(e => e.EpicId).IsRequired(false);
                entity.Property(e => e.SprintId).IsRequired(false);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Type).HasConversion<string>();
                entity.Property(e => e.Status).HasConversion<string>();
                entity.Property(e => e.Priority).HasConversion<string>();
                entity.Property(e => e.Origin).HasConversion<string>();
                entity.Property(e => e.AssignedToUserId).IsRequired(false);
                entity.Property(e => e.DateCreated).IsRequired();
                entity.Property(e => e.LastUpdated).IsRequired();
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.AssignedToUserId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<Project>().WithMany(p => p.ProductBacklogItems).HasForeignKey(e => e.ProjectId).IsRequired();
                entity.HasOne<Epic>().WithMany(e => e.ProductBacklogItems).HasForeignKey(e => e.EpicId).IsRequired(false);
                entity.HasOne<Sprint>().WithMany(e => e.ProductBacklogItems).HasForeignKey(e => e.SprintId).IsRequired(false);
            });

            modelBuilder.Entity<Epic>(entity =>
            {
                entity.ToTable("Epic");
                entity.HasKey(e => e.EpicId);
                entity.Property(e => e.EpicId).ValueGeneratedOnAdd();
                entity.Property(e => e.ProjectId).IsRequired();
                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.DateCreated).IsRequired();
                entity.HasOne<Project>().WithMany(p => p.Epics).HasForeignKey(e => e.ProjectId).IsRequired();
            });

            modelBuilder.Entity<Sprint>(entity =>
            {
                entity.ToTable("Sprint");
                entity.HasKey(e => e.SprintId);
                entity.Property(e => e.SprintId).ValueGeneratedOnAdd();
                entity.Property(e => e.ProjectId).IsRequired();
                entity.Property(e => e.SprintGoal);
                entity.Property(e => e.StartDate);
                entity.Property(e => e.EndDate);
                entity.Property(e => e.IsOpen);
                entity.Property(e => e.DateClosed);
                entity.HasOne<Project>().WithMany(p => p.Sprints).HasForeignKey(e => e.ProjectId).IsRequired();
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
                entity.HasOne<ProductBacklogItem>().WithMany(p => p.Comments).HasForeignKey(e => e.PbiId).IsRequired();
            });

            modelBuilder.Entity<PbiStatusHistory>(entity =>
            {
                entity.ToTable("PbiStatusHistory");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.PbiId).IsRequired();
                entity.Property(e => e.FromStatus).HasConversion<string>().IsRequired();
                entity.Property(e => e.ToStatus).HasConversion<string>().IsRequired();
                entity.Property(e => e.ChangedAt).IsRequired();
                entity.HasOne<ProductBacklogItem>().WithMany()
                    .HasForeignKey(e => e.PbiId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserDashboardPreference>(entity =>
            {
                entity.ToTable("UserDashboardPreferences");
                entity.HasKey(e => new { e.UserId, e.ProjectId });
                entity.Property(e => e.PreferencesJson).HasColumnType("TEXT");
                entity.HasOne<ApplicationUser>()
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Project>()
                    .WithMany()
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AudioTranscript>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.Transcript).IsRequired();
                entity.Property(e => e.RecordedAt).IsRequired();
            });

            // Prevent EF Core 10's complex-type convention from auto-mapping these as
            // owned JSON entities — the value converter below handles serialization instead.
            modelBuilder.Ignore<DiscordMessage>();
            modelBuilder.Ignore<DiscordAuthor>();

            modelBuilder.Entity<MessageTranscript>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                // Use a value converter so Messages serializes as a plain JSON string.
                var messagesProperty = entity.Property(e => e.Messages)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<List<DiscordMessage>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<DiscordMessage>()
                    );

                // Use jsonb for PostgreSQL, TEXT for SQLite
                if (Database.IsNpgsql())
                {
                    messagesProperty.HasColumnType("jsonb");
                }
                else
                {
                    messagesProperty.HasColumnType("TEXT");
                }

                messagesProperty.Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<DiscordMessage>>(
                    (c1, c2) => System.Text.Json.JsonSerializer.Serialize(c1, (System.Text.Json.JsonSerializerOptions?)null) == System.Text.Json.JsonSerializer.Serialize(c2, (System.Text.Json.JsonSerializerOptions?)null),
                    c => c == null ? 0 : System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
                    c => System.Text.Json.JsonSerializer.Deserialize<List<DiscordMessage>>(System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null), (System.Text.Json.JsonSerializerOptions?)null) ?? new List<DiscordMessage>()
                ));
            });
        }

        public override int SaveChanges()
            => SaveChanges(acceptAllChangesOnSuccess: true);

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            PrepareChangesForSave();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => await SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            PrepareChangesForSave();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void PrepareChangesForSave()
        {
            TrackStatusChanges();
            UpdateTimestamps();

            foreach (var entry in ChangeTracker.Entries<Organization>()
                         .Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                entry.Property(e => e.RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }

        private void TrackStatusChanges()
        {
            var modifiedPbis = ChangeTracker.Entries<ProductBacklogItem>()
                .Where(e => e.State == EntityState.Modified)
                .ToList();

            foreach (var entry in modifiedPbis)
            {
                var originalStatus = entry.Property(p => p.Status).OriginalValue;
                var currentStatus = entry.Entity.Status;
                if (originalStatus != currentStatus)
                {
                    PbiStatusHistories.Add(new PbiStatusHistory
                    {
                        PbiId = entry.Entity.PbiId,
                        FromStatus = originalStatus,
                        ToStatus = currentStatus,
                        ChangedAt = DateTime.UtcNow
                    });
                }
            }
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