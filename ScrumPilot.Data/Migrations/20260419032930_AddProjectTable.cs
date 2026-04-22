using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clear all seeded data before adding NOT NULL ProjectId columns.
            // Comments and PbiStatusHistory cascade from Stories, so Stories delete covers them.
            migrationBuilder.Sql("DELETE FROM \"Comment\"");
            migrationBuilder.Sql("DELETE FROM \"PbiStatusHistory\"");
            migrationBuilder.Sql("DELETE FROM \"Stories\"");
            migrationBuilder.Sql("DELETE FROM \"Epic\"");
            migrationBuilder.Sql("DELETE FROM \"Sprint\"");
            migrationBuilder.Sql("DELETE FROM \"UserDashboardPreferences\"");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserDashboardPreferences",
                table: "UserDashboardPreferences");

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "UserDashboardPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "Stories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "Sprint",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "Epic",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserDashboardPreferences",
                table: "UserDashboardPreferences",
                columns: new[] { "UserId", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "Project",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Project", x => x.ProjectId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stories_ProjectId",
                table: "Stories",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Sprint_ProjectId",
                table: "Sprint",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Epic_ProjectId",
                table: "Epic",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Epic_Project_ProjectId",
                table: "Epic",
                column: "ProjectId",
                principalTable: "Project",
                principalColumn: "ProjectId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Sprint_Project_ProjectId",
                table: "Sprint",
                column: "ProjectId",
                principalTable: "Project",
                principalColumn: "ProjectId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_Project_ProjectId",
                table: "Stories",
                column: "ProjectId",
                principalTable: "Project",
                principalColumn: "ProjectId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Epic_Project_ProjectId",
                table: "Epic");

            migrationBuilder.DropForeignKey(
                name: "FK_Sprint_Project_ProjectId",
                table: "Sprint");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_Project_ProjectId",
                table: "Stories");

            migrationBuilder.DropTable(
                name: "Project");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserDashboardPreferences",
                table: "UserDashboardPreferences");

            migrationBuilder.DropIndex(
                name: "IX_Stories_ProjectId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Sprint_ProjectId",
                table: "Sprint");

            migrationBuilder.DropIndex(
                name: "IX_Epic_ProjectId",
                table: "Epic");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "UserDashboardPreferences");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Sprint");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Epic");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserDashboardPreferences",
                table: "UserDashboardPreferences",
                column: "UserId");
        }
    }
}
