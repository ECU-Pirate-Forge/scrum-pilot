using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningArtifactApplicabilityReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanningArtifactReviews",
                columns: table => new
                {
                    PlanningArtifactReviewId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    PbiId = table.Column<int>(type: "integer", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningArtifactReviews", x => x.PlanningArtifactReviewId);
                    table.ForeignKey(
                        name: "FK_PlanningArtifactReviews_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanningArtifactReviews_Stories_ProjectId_PbiId",
                        columns: x => new { x.ProjectId, x.PbiId },
                        principalTable: "Stories",
                        principalColumns: new[] { "ProjectId", "PbiId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningArtifactReviews_ProjectId_PbiId_Fingerprint",
                table: "PlanningArtifactReviews",
                columns: new[] { "ProjectId", "PbiId", "Fingerprint" });
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                migrationBuilder.Sql("""
                    CREATE TRIGGER "TR_PlanningArtifactReviews_AppendOnly"
                    BEFORE UPDATE OR DELETE ON "PlanningArtifactReviews"
                    FOR EACH ROW EXECUTE FUNCTION planning_reject_mutation();
                    """);
            else
            {
                migrationBuilder.Sql("""
                    CREATE TRIGGER "TR_PlanningArtifactReviews_UPDATE" BEFORE UPDATE ON "PlanningArtifactReviews"
                    BEGIN SELECT RAISE(ABORT, 'Planning history is append-only'); END;
                    CREATE TRIGGER "TR_PlanningArtifactReviews_DELETE" BEFORE DELETE ON "PlanningArtifactReviews"
                    WHEN NOT EXISTS (SELECT 1 FROM "PlanningPurgeScopes" WHERE "ProjectId" = OLD."ProjectId")
                    BEGIN SELECT RAISE(ABORT, 'Planning history is append-only'); END;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanningArtifactReviews");
        }
    }
}
