using Microsoft.EntityFrameworkCore.Migrations;

namespace ScrumPilot.Data.Migrations.Organization;

/// <summary>Call Up after creating planning tables, and Down before dropping them.</summary>
public static class OrganizationPlanningSql
{
    public static IReadOnlyList<string> HistoryTables { get; } = Array.AsReadOnly(new[]
    {
        "ProjectGuidelinesRevisions", "TaskSpecificationRevisions", "TaskAllowedFiles",
        "GenerationInputs", "RepositorySnapshots", "TaskPullRequestBindings", "PullRequestScopeChecks",
        "NonTaskPullRequestExceptions", "DependencyReviewEvents", "PlanningArtifactReviews"
    });

    // Keep invalid/missing/cross-project legacy intent in DependsOnPbiId for explicit review.
    // A rerun must neither duplicate valid edges nor overwrite graph revisions.
    public const string Backfill = """
        UPDATE [Stories] SET [Version] = 1 WHERE [Version] = 0;
        INSERT INTO [PbiDependencies]
            ([ProjectId], [DependentPbiId], [PrerequisitePbiId], [ActorId], [CreatedAt])
        SELECT d.[ProjectId], d.[PbiId], p.[PbiId], N'legacy-migration', SYSUTCDATETIME()
        FROM [Stories] d
        INNER JOIN [Stories] p ON p.[PbiId] = d.[DependsOnPbiId] AND p.[ProjectId] = d.[ProjectId]
        WHERE d.[PbiId] <> p.[PbiId] AND NOT EXISTS (
            SELECT 1 FROM [PbiDependencies] e WHERE e.[ProjectId] = d.[ProjectId]
            AND e.[DependentPbiId] = d.[PbiId] AND e.[PrerequisitePbiId] = p.[PbiId]);
        INSERT INTO [ProjectGraphStates] ([ProjectId], [Revision])
        SELECT p.[ProjectId], 0 FROM [Project] p
        WHERE NOT EXISTS (SELECT 1 FROM [ProjectGraphStates] g WHERE g.[ProjectId] = p.[ProjectId]);
        """;

    public static IEnumerable<string> GuardStatements()
    {
        foreach (var table in HistoryTables)
            yield return $$"""
                CREATE OR ALTER TRIGGER [TR_{{table}}_AppendOnly] ON [{{table}}]
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM inserted)
                        THROW 51002, 'Planning history is append-only.', 1;
                    IF EXISTS (
                        SELECT 1 FROM deleted d
                        WHERE NOT EXISTS (
                            SELECT 1 FROM [PlanningPurgeScopes] s CROSS JOIN [TenantMetadata] m
                            WHERE s.[ProjectId] = d.[ProjectId]
                              AND s.[TransactionId] = CURRENT_TRANSACTION_ID()
                              AND m.[Id] = 1
                              AND APPLOCK_MODE('public',
                                  CONCAT('planning-purge:', m.[StorageKey], ':', d.[ProjectId]),
                                  'Transaction') = 'Exclusive'))
                        THROW 51002, 'Planning history requires authorized scoped deletion.', 1;
                END;
                """;
        yield return """
            CREATE OR ALTER TRIGGER [TR_TenantMetadata_Immutable] ON [TenantMetadata]
            AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (SELECT 1 FROM deleted)
                    THROW 51003, 'Tenant storage identity is immutable.', 1;
            END;
            """;
        yield return """
            CREATE OR ALTER TRIGGER [TR_GenerationRuns_Provenance] ON [GenerationRuns]
            AFTER UPDATE AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT [GenerationRunId], [ProjectId], [Kind], [Provider], [Model], [IdempotencyKey], [ActorId], [CreatedAt] FROM deleted
                    EXCEPT
                    SELECT [GenerationRunId], [ProjectId], [Kind], [Provider], [Model], [IdempotencyKey], [ActorId], [CreatedAt] FROM inserted)
                    THROW 51002, 'Generation provenance is immutable.', 1;
            END;
            """;
    }

    public static void Up(MigrationBuilder migration)
    {
        migration.Sql(Backfill);
        foreach (var sql in GuardStatements()) migration.Sql(sql);
    }

    public static void Down(MigrationBuilder migration)
    {
        foreach (var table in HistoryTables)
            migration.Sql($"DROP TRIGGER IF EXISTS [TR_{table}_AppendOnly];");
        migration.Sql("DROP TRIGGER IF EXISTS [TR_TenantMetadata_Immutable];");
        migration.Sql("DROP TRIGGER IF EXISTS [TR_GenerationRuns_Provenance];");
    }
}
