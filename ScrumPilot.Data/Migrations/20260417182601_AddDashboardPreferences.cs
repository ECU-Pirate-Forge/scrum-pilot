using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserDashboardPreferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PreferencesJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDashboardPreferences", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDashboardPreferences");
        }
    }
}
