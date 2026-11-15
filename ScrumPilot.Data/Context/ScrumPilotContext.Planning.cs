using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Context;

public partial class ScrumPilotContext
{
    public DbSet<ProjectGuidelinesRevision> ProjectGuidelinesRevisions => Set<ProjectGuidelinesRevision>();
    public DbSet<ProblemStatement> ProblemStatements => Set<ProblemStatement>();
    public DbSet<GenerationRun> GenerationRuns => Set<GenerationRun>();
    public DbSet<GenerationInput> GenerationInputs => Set<GenerationInput>();
    public DbSet<ProjectRepository> ProjectRepositories => Set<ProjectRepository>();
    public DbSet<RepositorySnapshot> RepositorySnapshots => Set<RepositorySnapshot>();
    public DbSet<TaskSpecificationRevision> TaskSpecificationRevisions => Set<TaskSpecificationRevision>();
    public DbSet<TaskAllowedFile> TaskAllowedFiles => Set<TaskAllowedFile>();
    public DbSet<PbiDependency> PbiDependencies => Set<PbiDependency>();
    public DbSet<DependencyReview> DependencyReviews => Set<DependencyReview>();
    public DbSet<DependencyReviewEvent> DependencyReviewEvents => Set<DependencyReviewEvent>();
    public DbSet<ProjectGraphState> ProjectGraphStates => Set<ProjectGraphState>();
    public DbSet<TaskPullRequestBinding> TaskPullRequestBindings => Set<TaskPullRequestBinding>();
    public DbSet<PullRequestScopeCheck> PullRequestScopeChecks => Set<PullRequestScopeCheck>();
    public DbSet<NonTaskPullRequestException> NonTaskPullRequestExceptions => Set<NonTaskPullRequestException>();
    public DbSet<GitHubWebhookDelivery> GitHubWebhookDeliveries => Set<GitHubWebhookDelivery>();

    private static void ConfigurePlanning(ModelBuilder model)
    {
        model.Entity<PlanningPurgeScope>().HasKey(scope => scope.ProjectId);
        model.Entity<PlanningPurgeScope>().Property(scope => scope.ProjectId).ValueGeneratedNever();
        model.Entity<PlanningPurgeScope>().ToTable("PlanningPurgeScopes");
        model.Entity<ProductBacklogItem>().HasAlternateKey(x => new { x.ProjectId, x.PbiId });
        model.Entity<ProductBacklogItem>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<ProductBacklogItem>().Property(x => x.GenerationCandidateKey).HasMaxLength(100);
        model.Entity<ProductBacklogItem>().HasIndex(x => new { x.GenerationRunId, x.GenerationCandidateKey }).IsUnique();
        model.Entity<ProductBacklogItem>().HasOne<ProductBacklogItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.ParentStoryId })
            .HasPrincipalKey(x => new { x.ProjectId, x.PbiId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductBacklogItem>().HasOne<ProblemStatement>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.ProblemStatementId })
            .HasPrincipalKey(x => new { x.ProjectId, x.ProblemStatementId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductBacklogItem>().HasOne<GenerationRun>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.GenerationRunId })
            .HasPrincipalKey(x => new { x.ProjectId, x.GenerationRunId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ProductBacklogItem>().ToTable("Stories", t =>
            t.HasCheckConstraint("CK_Stories_Parent", "\"ParentStoryId\" IS NULL OR \"ParentStoryId\" <> \"PbiId\""));

        var guidelines = model.Entity<ProjectGuidelinesRevision>();
        Scope(guidelines, nameof(ProjectGuidelinesRevision.ProjectGuidelinesRevisionId));
        guidelines.HasIndex(x => new { x.ProjectId, x.Revision }).IsUnique();
        guidelines.ToTable(t => t.HasCheckConstraint("CK_Guidelines_Revision", "\"Revision\" > 0"));
        guidelines.Property(x => x.ContentHash).HasMaxLength(64);
        model.Entity<Project>().HasOne<ProjectGuidelinesRevision>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.CurrentGuidelinesRevisionId })
            .HasPrincipalKey(x => new { x.ProjectId, x.ProjectGuidelinesRevisionId }).OnDelete(DeleteBehavior.Restrict);
        // Npgsql otherwise removes identity generation when this key also participates in the pointer FK.
        model.Entity<Project>().Property(x => x.ProjectId).UseIdentityByDefaultColumn();

        var problems = model.Entity<ProblemStatement>();
        Scope(problems, nameof(ProblemStatement.ProblemStatementId));
        problems.Property(x => x.Title).HasMaxLength(200);
        problems.Property(x => x.Version).IsConcurrencyToken();
        problems.HasIndex(x => new { x.ProjectId, x.IsArchived });

        var runs = model.Entity<GenerationRun>();
        Scope(runs, nameof(GenerationRun.GenerationRunId));
        runs.Property(x => x.Kind).HasConversion<string>();
        runs.Property(x => x.State).HasConversion<string>();
        runs.Property(x => x.IdempotencyKey).HasMaxLength(200);
        runs.Property(x => x.Provider).HasMaxLength(100);
        runs.Property(x => x.Version).IsConcurrencyToken();
        runs.HasIndex(x => new { x.ProjectId, x.Kind, x.IdempotencyKey }).IsUnique();

        var repositories = model.Entity<ProjectRepository>();
        Scope(repositories, nameof(ProjectRepository.ProjectRepositoryId));
        repositories.Property(x => x.Version).IsConcurrencyToken();
        repositories.HasIndex(x => new { x.ProjectId, x.GitHubRepositoryId }).IsUnique();
        repositories.HasIndex(x => x.ProjectId).IsUnique();
        repositories.HasIndex(x => x.GitHubRepositoryId).IsUnique();
        repositories.Property(x => x.Owner).HasMaxLength(100);
        repositories.Property(x => x.Name).HasMaxLength(100);

        var snapshots = model.Entity<RepositorySnapshot>();
        Scope(snapshots, nameof(RepositorySnapshot.RepositorySnapshotId));
        snapshots.HasOne<ProjectRepository>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.ProjectRepositoryId })
            .HasPrincipalKey(x => new { x.ProjectId, x.ProjectRepositoryId }).OnDelete(DeleteBehavior.Restrict);
        snapshots.HasIndex(x => new { x.ProjectRepositoryId, x.Branch, x.CommitSha }).IsUnique();
        snapshots.Property(x => x.CommitSha).HasMaxLength(64);
        snapshots.Property(x => x.TreeSha).HasMaxLength(64);

        var inputs = model.Entity<GenerationInput>();
        Scope(inputs, nameof(GenerationInput.GenerationInputId));
        inputs.HasIndex(x => x.GenerationRunId).IsUnique();
        inputs.HasOne<GenerationRun>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.GenerationRunId })
            .HasPrincipalKey(x => new { x.ProjectId, x.GenerationRunId }).OnDelete(DeleteBehavior.Restrict);
        inputs.HasOne<ProjectGuidelinesRevision>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.ProjectGuidelinesRevisionId })
            .HasPrincipalKey(x => new { x.ProjectId, x.ProjectGuidelinesRevisionId }).OnDelete(DeleteBehavior.Restrict);
        inputs.HasOne<ProblemStatement>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.ProblemStatementId })
            .HasPrincipalKey(x => new { x.ProjectId, x.ProblemStatementId }).OnDelete(DeleteBehavior.Restrict);
        inputs.HasOne<RepositorySnapshot>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.RepositorySnapshotId })
            .HasPrincipalKey(x => new { x.ProjectId, x.RepositorySnapshotId }).OnDelete(DeleteBehavior.Restrict);

        var specs = model.Entity<TaskSpecificationRevision>();
        Scope(specs, nameof(TaskSpecificationRevision.TaskSpecificationRevisionId));
        specs.HasIndex(x => new { x.PbiId, x.Revision }).IsUnique();
        specs.ToTable(t => t.HasCheckConstraint("CK_Specification_Revision", "\"Revision\" > 0"));
        specs.Property(x => x.ScopeMode).HasConversion<string>();
        specs.Property(x => x.Approval).HasConversion<string>();
        specs.Property(x => x.ContentHash).HasMaxLength(64);
        specs.Property(x => x.Content).HasConversion(
            x => SerializeContent(x), x => DeserializeContent(x))
            .Metadata.SetValueComparer(new ValueComparer<TaskSpecificationContent>(
                (a, b) => SerializeContent(a!) == SerializeContent(b!),
                x => SerializeContent(x).GetHashCode(),
                x => DeserializeContent(SerializeContent(x))));
        specs.HasOne<ProductBacklogItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.PbiId })
            .HasPrincipalKey(x => new { x.ProjectId, x.PbiId }).OnDelete(DeleteBehavior.Restrict);
        specs.HasOne<GenerationRun>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.GenerationRunId })
            .HasPrincipalKey(x => new { x.ProjectId, x.GenerationRunId }).OnDelete(DeleteBehavior.Restrict);
        specs.HasOne<ProjectGuidelinesRevision>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.ProjectGuidelinesRevisionId })
            .HasPrincipalKey(x => new { x.ProjectId, x.ProjectGuidelinesRevisionId }).OnDelete(DeleteBehavior.Restrict);
        specs.HasOne<RepositorySnapshot>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.RepositorySnapshotId })
            .HasPrincipalKey(x => new { x.ProjectId, x.RepositorySnapshotId }).OnDelete(DeleteBehavior.Restrict);

        var files = model.Entity<TaskAllowedFile>();
        Scope(files, nameof(TaskAllowedFile.TaskAllowedFileId));
        files.Property(x => x.Category).HasConversion<string>();
        files.Property(x => x.Operation).HasConversion<string>();
        files.Property(x => x.Path).HasMaxLength(1024);
        files.Property(x => x.RenameFromPath).HasMaxLength(1024);
        files.HasIndex(x => new { x.TaskSpecificationRevisionId, x.Path }).IsUnique();
        files.HasOne<TaskSpecificationRevision>().WithMany(x => x.AllowedFiles)
            .HasForeignKey(x => new { x.ProjectId, x.TaskSpecificationRevisionId })
            .HasPrincipalKey(x => new { x.ProjectId, x.TaskSpecificationRevisionId }).OnDelete(DeleteBehavior.Restrict);
        files.ToTable(t => t.HasCheckConstraint("CK_AllowedFile_Rename",
            "(\"Operation\" = 'Rename' AND \"RenameFromPath\" IS NOT NULL) OR (\"Operation\" <> 'Rename' AND \"RenameFromPath\" IS NULL)"));

        var edges = model.Entity<PbiDependency>();
        edges.HasKey(x => new { x.ProjectId, x.DependentPbiId, x.PrerequisitePbiId });
        edges.HasOne<ProductBacklogItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.DependentPbiId })
            .HasPrincipalKey(x => new { x.ProjectId, x.PbiId }).OnDelete(DeleteBehavior.Restrict);
        edges.HasOne<ProductBacklogItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.PrerequisitePbiId })
            .HasPrincipalKey(x => new { x.ProjectId, x.PbiId }).OnDelete(DeleteBehavior.Restrict);
        edges.ToTable(t => t.HasCheckConstraint("CK_Dependency_NotSelf", "\"DependentPbiId\" <> \"PrerequisitePbiId\""));

        var reviews = model.Entity<DependencyReview>();
        reviews.HasKey(x => new { x.ProjectId, x.PbiId });
        reviews.Property(x => x.Version).IsConcurrencyToken();
        reviews.HasOne<ProductBacklogItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.PbiId })
            .HasPrincipalKey(x => new { x.ProjectId, x.PbiId }).OnDelete(DeleteBehavior.Restrict);
        reviews.ToTable(t => t.HasCheckConstraint("CK_DependencyReview_Revision",
            "\"InvalidationRevision\" >= 0 AND (\"ReviewedRevision\" IS NULL OR (\"ReviewedRevision\" >= 0 AND \"ReviewedRevision\" <= \"InvalidationRevision\"))"));
        var decisions = model.Entity<DependencyReviewEvent>();
        Scope(decisions, nameof(DependencyReviewEvent.DependencyReviewEventId));
        decisions.HasIndex(item => new { item.ProjectId, item.PbiId, item.InvalidationRevision });
        decisions.HasOne<ProductBacklogItem>().WithMany().HasForeignKey(item => new { item.ProjectId, item.PbiId })
            .HasPrincipalKey(item => new { item.ProjectId, item.PbiId }).OnDelete(DeleteBehavior.Restrict);

        var graph = model.Entity<ProjectGraphState>();
        graph.HasKey(x => x.ProjectId);
        graph.Property(x => x.ProjectId).ValueGeneratedNever();
        graph.Property(x => x.Revision).IsConcurrencyToken();
        // Graph state alone is not historical content and must not prevent legacy project deletion.
        graph.HasOne<Project>().WithOne().HasForeignKey<ProjectGraphState>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);

        var bindings = model.Entity<TaskPullRequestBinding>();
        Scope(bindings, nameof(TaskPullRequestBinding.TaskPullRequestBindingId));
        bindings.HasIndex(x => new { x.PbiId, x.ProjectRepositoryId, x.PullRequestNumber }).IsUnique();
        bindings.HasIndex(x => new { x.ProjectRepositoryId, x.PullRequestNumber }).IsUnique();
        bindings.HasOne<ProductBacklogItem>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.PbiId })
            .HasPrincipalKey(x => new { x.ProjectId, x.PbiId }).OnDelete(DeleteBehavior.Restrict);
        bindings.HasOne<ProjectRepository>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.ProjectRepositoryId })
            .HasPrincipalKey(x => new { x.ProjectId, x.ProjectRepositoryId }).OnDelete(DeleteBehavior.Restrict);
        bindings.ToTable(t => t.HasCheckConstraint("CK_Binding_Number", "\"PullRequestNumber\" > 0"));

        var checks = model.Entity<PullRequestScopeCheck>();
        Scope(checks, nameof(PullRequestScopeCheck.PullRequestScopeCheckId));
        checks.Property(x => x.State).HasConversion<string>();
        checks.Property(x => x.HeadSha).HasMaxLength(64);
        checks.HasOne<TaskPullRequestBinding>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.TaskPullRequestBindingId })
            .HasPrincipalKey(x => new { x.ProjectId, x.TaskPullRequestBindingId }).OnDelete(DeleteBehavior.Restrict);
        checks.HasOne<TaskSpecificationRevision>().WithMany()
            .HasForeignKey(x => new { x.ProjectId, x.TaskSpecificationRevisionId })
            .HasPrincipalKey(x => new { x.ProjectId, x.TaskSpecificationRevisionId }).OnDelete(DeleteBehavior.Restrict);
        checks.HasIndex(x => new { x.TaskPullRequestBindingId, x.HeadSha, x.TaskSpecificationRevisionId, x.CreatedAt });

        var exceptions = model.Entity<NonTaskPullRequestException>();
        Scope(exceptions, nameof(NonTaskPullRequestException.NonTaskPullRequestExceptionId));
        exceptions.HasIndex(x => new { x.ProjectRepositoryId, x.PullRequestNumber }).IsUnique();
        exceptions.HasOne<ProjectRepository>().WithMany().HasForeignKey(x => new { x.ProjectId, x.ProjectRepositoryId })
            .HasPrincipalKey(x => new { x.ProjectId, x.ProjectRepositoryId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<GitHubWebhookDelivery>().HasKey(x => x.DeliveryId);
        model.Entity<GitHubWebhookDelivery>().Property(x => x.DeliveryId).HasMaxLength(100);
    }

    private static void Scope<T>(EntityTypeBuilder<T> entity, string id) where T : class
    {
        entity.HasKey(id);
        entity.Property<int>(id).ValueGeneratedOnAdd();
        entity.HasAlternateKey("ProjectId", id);
        entity.HasOne<Project>().WithMany().HasForeignKey("ProjectId").OnDelete(DeleteBehavior.Restrict);
    }

    private static string SerializeContent(TaskSpecificationContent content) => JsonSerializer.Serialize(content);
    private static TaskSpecificationContent DeserializeContent(string json) =>
        JsonSerializer.Deserialize<TaskSpecificationContent>(json)!;
    private static string Hash(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private void PreparePlanningChanges()
    {
        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            var immutable = entry.Entity is ProjectGuidelinesRevision or TaskSpecificationRevision
                or TaskAllowedFile or GenerationInput or RepositorySnapshot or TaskPullRequestBinding or PullRequestScopeCheck
                or NonTaskPullRequestException or GitHubWebhookDelivery or DependencyReviewEvent;
            if (immutable && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} is append-only.");
            if (entry.Entity is GenerationRun && entry.State == EntityState.Modified &&
                entry.Properties.Any(p => p.IsModified && p.Metadata.Name is not
                    (nameof(GenerationRun.State) or nameof(GenerationRun.CompletedAt) or
                     nameof(GenerationRun.ErrorCode) or nameof(GenerationRun.Version))))
                throw new InvalidOperationException("Generation run provenance is immutable; only execution state can change.");

            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            if (entry.Entity is ProductBacklogItem or ProblemStatement or GenerationRun or ProjectRepository or DependencyReview)
            {
                var version = entry.Property("Version");
                version.CurrentValue = entry.State == EntityState.Added ? 1L : checked((long)version.OriginalValue! + 1);
            }
            if (entry.Entity is ProjectGraphState)
            {
                var revision = entry.Property("Revision");
                revision.CurrentValue = entry.State == EntityState.Added ? 0L : checked((long)revision.OriginalValue! + 1);
            }
            if (entry.State != EntityState.Added)
                continue;
            switch (entry.Entity)
            {
                case ProjectGuidelinesRevision g:
                    g.ContentHash = Hash(g.Content);
                    break;
                case GenerationInput i:
                    i.ContentHash = Hash(i.SnapshotJson);
                    break;
                case TaskSpecificationRevision s:
                    s.ContentHash = Hash(JsonSerializer.Serialize(new
                    {
                        s.ProjectId,
                        s.PbiId,
                        s.RepositorySnapshotId,
                        s.ProjectGuidelinesRevisionId,
                        s.GenerationRunId,
                        s.Content,
                        s.ScopeMode,
                        AllowedFiles = s.AllowedFiles.OrderBy(f => f.Path, StringComparer.Ordinal)
                            .Select(f => new { f.Path, f.Category, f.Operation, f.RenameFromPath, f.Rationale }).ToArray()
                    }));
                    break;
                case TaskAllowedFile f:
                    ValidateExactPath(f.Path);
                    if (f.RenameFromPath is not null) ValidateExactPath(f.RenameFromPath);
                    // A persisted specification's scope cannot be extended after its initial save.
                    if (!ChangeTracker.Entries<TaskSpecificationRevision>().Any(s =>
                        s.State == EntityState.Added && s.Entity.ProjectId == f.ProjectId &&
                        s.Entity.TaskSpecificationRevisionId == f.TaskSpecificationRevisionId))
                        throw new InvalidOperationException("Allowed files must be added with their specification revision.");
                    break;
            }
        }
    }

    private static void ValidateExactPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') ||
            path.IndexOfAny(['*', '?']) >= 0 || path.Any(char.IsControl) ||
            path.Split('/').Any(s => s is "" or "." or ".."))
            throw new InvalidOperationException("Allowed files require exact repository-relative slash-separated paths.");
    }
}
