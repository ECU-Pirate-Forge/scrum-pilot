using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompletePlanningScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepositorySnapshots_ProjectRepositoryId_CommitSha",
                table: "RepositorySnapshots");

            migrationBuilder.AddColumn<string>(
                name: "ActorId",
                table: "TaskPullRequestBindings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GenerationCandidateKey",
                table: "Stories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Branch",
                table: "RepositorySnapshots",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BaseSha",
                table: "PullRequestScopeChecks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "GitHubCheckId",
                table: "PullRequestScopeChecks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MergeBaseSha",
                table: "PullRequestScopeChecks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ActorId",
                table: "PbiDependencies",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PbiDependencies",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.CreateTable(
                name: "DependencyReviewEvents",
                columns: table => new
                {
                    DependencyReviewEventId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    PbiId = table.Column<int>(type: "integer", nullable: false),
                    InvalidationRevision = table.Column<long>(type: "bigint", nullable: false),
                    Accepted = table.Column<bool>(type: "boolean", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependencyReviewEvents", x => x.DependencyReviewEventId);
                    table.UniqueConstraint("AK_DependencyReviewEvents_ProjectId_DependencyReviewEventId", x => new { x.ProjectId, x.DependencyReviewEventId });
                    table.ForeignKey(
                        name: "FK_DependencyReviewEvents_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DependencyReviewEvents_Stories_ProjectId_PbiId",
                        columns: x => new { x.ProjectId, x.PbiId },
                        principalTable: "Stories",
                        principalColumns: new[] { "ProjectId", "PbiId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GitHubWebhookDeliveries",
                columns: table => new
                {
                    DeliveryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubWebhookDeliveries", x => x.DeliveryId);
                });

            migrationBuilder.CreateTable(
                name: "NonTaskPullRequestExceptions",
                columns: table => new
                {
                    NonTaskPullRequestExceptionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ProjectRepositoryId = table.Column<int>(type: "integer", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonTaskPullRequestExceptions", x => x.NonTaskPullRequestExceptionId);
                    table.UniqueConstraint("AK_NonTaskPullRequestExceptions_ProjectId_NonTaskPullRequestEx~", x => new { x.ProjectId, x.NonTaskPullRequestExceptionId });
                    table.ForeignKey(
                        name: "FK_NonTaskPullRequestExceptions_ProjectRepositories_ProjectId_~",
                        columns: x => new { x.ProjectId, x.ProjectRepositoryId },
                        principalTable: "ProjectRepositories",
                        principalColumns: new[] { "ProjectId", "ProjectRepositoryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonTaskPullRequestExceptions_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanningPurgeScopes",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningPurgeScopes", x => x.ProjectId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskPullRequestBindings_ProjectRepositoryId_PullRequestNumb~",
                table: "TaskPullRequestBindings",
                columns: new[] { "ProjectRepositoryId", "PullRequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stories_GenerationRunId_GenerationCandidateKey",
                table: "Stories",
                columns: new[] { "GenerationRunId", "GenerationCandidateKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositorySnapshots_ProjectRepositoryId_Branch_CommitSha",
                table: "RepositorySnapshots",
                columns: new[] { "ProjectRepositoryId", "Branch", "CommitSha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_GitHubRepositoryId",
                table: "ProjectRepositories",
                column: "GitHubRepositoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_ProjectId",
                table: "ProjectRepositories",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DependencyReviewEvents_ProjectId_PbiId_InvalidationRevision",
                table: "DependencyReviewEvents",
                columns: new[] { "ProjectId", "PbiId", "InvalidationRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_NonTaskPullRequestExceptions_ProjectId_ProjectRepositoryId",
                table: "NonTaskPullRequestExceptions",
                columns: new[] { "ProjectId", "ProjectRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_NonTaskPullRequestExceptions_ProjectRepositoryId_PullReques~",
                table: "NonTaskPullRequestExceptions",
                columns: new[] { "ProjectRepositoryId", "PullRequestNumber" },
                unique: true);
            ConfigureHistoryGuards(migrationBuilder, true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ConfigureHistoryGuards(migrationBuilder, false);
            migrationBuilder.DropTable(
                name: "DependencyReviewEvents");

            migrationBuilder.DropTable(
                name: "GitHubWebhookDeliveries");

            migrationBuilder.DropTable(
                name: "NonTaskPullRequestExceptions");

            migrationBuilder.DropTable(
                name: "PlanningPurgeScopes");

            migrationBuilder.DropIndex(
                name: "IX_TaskPullRequestBindings_ProjectRepositoryId_PullRequestNumb~",
                table: "TaskPullRequestBindings");

            migrationBuilder.DropIndex(
                name: "IX_Stories_GenerationRunId_GenerationCandidateKey",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_RepositorySnapshots_ProjectRepositoryId_Branch_CommitSha",
                table: "RepositorySnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ProjectRepositories_GitHubRepositoryId",
                table: "ProjectRepositories");

            migrationBuilder.DropIndex(
                name: "IX_ProjectRepositories_ProjectId",
                table: "ProjectRepositories");

            foreach (var (table, column) in new[]
                     {
                         ("TaskPullRequestBindings", "ActorId"), ("Stories", "GenerationCandidateKey"),
                         ("RepositorySnapshots", "Branch"), ("PullRequestScopeChecks", "BaseSha"),
                         ("PullRequestScopeChecks", "GitHubCheckId"), ("PullRequestScopeChecks", "MergeBaseSha"),
                         ("PbiDependencies", "ActorId"), ("PbiDependencies", "CreatedAt")
                     })
            {
                if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
                    migrationBuilder.Sql($"""ALTER TABLE "{table}" DROP COLUMN "{column}";""");
                else migrationBuilder.DropColumn(name: column, table: table);
            }

            migrationBuilder.CreateIndex(
                name: "IX_RepositorySnapshots_ProjectRepositoryId_CommitSha",
                table: "RepositorySnapshots",
                columns: new[] { "ProjectRepositoryId", "CommitSha" },
                unique: true);
        }

        private static readonly string[] ExistingHistory =
        [
            "ProjectGuidelinesRevisions", "TaskSpecificationRevisions", "TaskAllowedFiles",
            "GenerationInputs", "RepositorySnapshots", "TaskPullRequestBindings", "PullRequestScopeChecks"
        ];
        private static readonly string[] AdditionalHistory = ["NonTaskPullRequestExceptions", "DependencyReviewEvents"];

        private void ConfigureHistoryGuards(MigrationBuilder builder, bool allowScopedPurge)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                var deletion = allowScopedPurge ? """
                    IF TG_OP = 'DELETE' AND EXISTS (
                        SELECT 1 FROM "PlanningPurgeScopes" WHERE "ProjectId" = OLD."ProjectId")
                    THEN RETURN OLD; END IF;
                    """ : "";
                builder.Sql($$"""
                    CREATE OR REPLACE FUNCTION planning_reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                        {{deletion}}
                        RAISE EXCEPTION 'Planning history is append-only';
                    END;
                    $$;
                    """);
                foreach (var table in allowScopedPurge ? AdditionalHistory : [])
                    builder.Sql($"""
                        CREATE TRIGGER "TR_{table}_AppendOnly" BEFORE UPDATE OR DELETE ON "{table}"
                        FOR EACH ROW EXECUTE FUNCTION planning_reject_mutation();
                        """);
            }
            else
            {
                foreach (var table in ExistingHistory.Concat(allowScopedPurge ? AdditionalHistory : []))
                {
                    foreach (var operation in new[] { "DELETE", "UPDATE" })
                    {
                        builder.Sql($"""DROP TRIGGER IF EXISTS "TR_{table}_{operation}";""");
                        var condition = allowScopedPurge && operation == "DELETE"
                            ? """WHEN NOT EXISTS (SELECT 1 FROM "PlanningPurgeScopes" WHERE "ProjectId" = OLD."ProjectId")"""
                            : "";
                        builder.Sql($"""
                            CREATE TRIGGER "TR_{table}_{operation}" BEFORE {operation} ON "{table}" {condition}
                            BEGIN SELECT RAISE(ABORT, 'Planning history is append-only'); END;
                            """);
                    }
                }
            }
        }
    }
}
