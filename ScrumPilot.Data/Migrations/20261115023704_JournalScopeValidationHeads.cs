using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class JournalScopeValidationHeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PullRequestScopeAttempts",
                columns: table => new
                {
                    PullRequestScopeAttemptId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ProjectRepositoryId = table.Column<int>(type: "integer", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "integer", nullable: false),
                    HeadSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestScopeAttempts", x => x.PullRequestScopeAttemptId);
                    table.ForeignKey(
                        name: "FK_PullRequestScopeAttempts_ProjectRepositories_ProjectId_Proj~",
                        columns: x => new { x.ProjectId, x.ProjectRepositoryId },
                        principalTable: "ProjectRepositories",
                        principalColumns: new[] { "ProjectId", "ProjectRepositoryId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScopeAttempts_ProjectId_ProjectRepositoryId",
                table: "PullRequestScopeAttempts",
                columns: new[] { "ProjectId", "ProjectRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScopeAttempts_ProjectRepositoryId_PullRequestNum~",
                table: "PullRequestScopeAttempts",
                columns: new[] { "ProjectRepositoryId", "PullRequestNumber" });
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                migrationBuilder.Sql("""
                    CREATE TRIGGER "TR_PullRequestScopeAttempts_AppendOnly"
                    BEFORE UPDATE OR DELETE ON "PullRequestScopeAttempts"
                    FOR EACH ROW EXECUTE FUNCTION planning_reject_mutation();
                    """);
            else
                migrationBuilder.Sql("""
                    CREATE TRIGGER "TR_PullRequestScopeAttempts_UPDATE" BEFORE UPDATE ON "PullRequestScopeAttempts"
                    BEGIN SELECT RAISE(ABORT, 'Planning history is append-only'); END;
                    CREATE TRIGGER "TR_PullRequestScopeAttempts_DELETE" BEFORE DELETE ON "PullRequestScopeAttempts"
                    WHEN NOT EXISTS (SELECT 1 FROM "PlanningPurgeScopes" WHERE "ProjectId" = OLD."ProjectId")
                    BEGIN SELECT RAISE(ABORT, 'Planning history is append-only'); END;
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PullRequestScopeAttempts");
        }
    }
}
