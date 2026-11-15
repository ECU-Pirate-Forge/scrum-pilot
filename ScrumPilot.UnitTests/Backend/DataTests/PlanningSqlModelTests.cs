using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Migrations.Organization;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class PlanningSqlModelTests
{
    [Fact]
    public void PlanningIsTenantOnly_AndIdentityAndJournalAreControlOnly()
    {
        using var tenant = new OrganizationContextFactory().CreateDbContext([]);
        using var control = new ControlPlaneContextFactory().CreateDbContext([]);
        foreach (var type in new[] { typeof(ProjectGuidelinesRevision), typeof(GenerationRun), typeof(GenerationInput),
                     typeof(ProblemStatement), typeof(ProjectRepository), typeof(RepositorySnapshot),
                     typeof(TaskSpecificationRevision), typeof(TaskAllowedFile), typeof(PbiDependency),
                     typeof(DependencyReview), typeof(DependencyReviewEvent), typeof(ProjectGraphState),
                     typeof(TaskPullRequestBinding), typeof(PullRequestScopeCheck),
                     typeof(NonTaskPullRequestException), typeof(PlanningArtifactReview) })
        {
            Assert.NotNull(tenant.Model.FindEntityType(type));
            Assert.Null(control.Model.FindEntityType(type));
        }
        Assert.Null(tenant.Model.FindEntityType(typeof(PullRequestScopeAttempt)));
        Assert.Null(tenant.Model.FindEntityType(typeof(GitHubWebhookDelivery)));
        Assert.Null(tenant.Model.FindEntityType(typeof(Organization)));
        Assert.NotNull(control.Model.FindEntityType(typeof(PullRequestScopeAttempt)));
        Assert.NotNull(control.Model.FindEntityType(typeof(GitHubWebhookDelivery)));
        Assert.DoesNotContain(typeof(ScrumPilotContext).GetProperties(),
            p => p.Name is "ProjectGuidelinesRevisions" or "TaskSpecificationRevisions" or "GenerationRuns");
    }

    [Fact]
    public void SqlModel_UsesBoundedIndexesOrdinalPathsAndFilteredNullableKeys()
    {
        using var context = new OrganizationContextFactory().CreateDbContext([]);
        var model = context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var files = model.FindEntityType(typeof(TaskAllowedFile))!;
        Assert.Equal(1024, files.FindProperty(nameof(TaskAllowedFile.Path))!.GetMaxLength());
        Assert.Equal("Latin1_General_100_BIN2", files.FindProperty(nameof(TaskAllowedFile.Path))!.GetCollation());
        Assert.Contains(files.GetIndexes(), x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(["TaskSpecificationRevisionId", "PathHash"]));
        Assert.Contains("HASHBYTES", files.FindProperty("PathHash")!.GetComputedColumnSql());
        Assert.All(files.GetIndexes().Where(i => i.Properties.Any(p => p.Name == "PathHash")),
            i => Assert.Null(i.GetFilter()));
        var candidates = model.FindEntityType(typeof(ProductBacklogItem))!.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(["GenerationRunId", "GenerationCandidateKey"]));
        Assert.Equal("[GenerationRunId] IS NOT NULL AND [GenerationCandidateKey] IS NOT NULL", candidates.GetFilter());
        foreach (var index in model.GetEntityTypes().SelectMany(e => e.GetIndexes()))
        {
            var bytes = index.Properties.Sum(p => p.ClrType == typeof(string)
                ? 2 * (p.GetMaxLength() ?? 450) : p.ClrType == typeof(byte[]) ? (p.GetMaxLength() ?? 32) : 8);
            Assert.True(bytes <= 1700, $"Oversized SQL index: {index.DeclaringEntityType.Name}");
        }
    }

    [Fact]
    public async Task GuardsRunBeforeAnyConnection_AndPolicyRequiresTransaction()
    {
        using var context = new OrganizationContextFactory().CreateDbContext([]);
        var revision = new ProjectGuidelinesRevision { ProjectGuidelinesRevisionId = 1, ProjectId = 1, Revision = 1, Content = "History", ActorId = "test" };
        context.Attach(revision);
        revision.Content = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        context.Attach(new TenantMetadata { OrganizationId = 1, StorageKey = new string('a', 32) }).Property(x => x.StorageKey).CurrentValue = new string('b', 32);
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.AcquirePlanningPolicyLockAsync(1, default));
    }

    [Fact]
    public void HistoryTriggers_AreSetBasedAndRequireCurrentTransactionAndScopedLock()
    {
        var statements = OrganizationPlanningSql.GuardStatements().ToArray();
        foreach (var sql in statements.Take(OrganizationPlanningSql.HistoryTables.Count))
        {
            Assert.Contains("SELECT 1 FROM inserted", sql);
            Assert.Contains("SELECT 1 FROM deleted d", sql);
            Assert.Contains("s.[ProjectId] = d.[ProjectId]", sql);
            Assert.Contains("CURRENT_TRANSACTION_ID()", sql);
            Assert.Contains("APPLOCK_MODE", sql);
            Assert.Contains("'Transaction') = 'Exclusive'", sql);
        }
        Assert.DoesNotContain(statements, s => s.Contains("PullRequestScopeAttempt", StringComparison.Ordinal));
        Assert.DoesNotContain(statements, s => s.Contains("GitHubWebhookDelivery", StringComparison.Ordinal));
    }
}
