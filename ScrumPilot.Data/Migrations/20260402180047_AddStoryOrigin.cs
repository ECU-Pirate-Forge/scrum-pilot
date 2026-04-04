using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAiGenerated",
                table: "Stories");

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Stories",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Stories");

            migrationBuilder.AddColumn<bool>(
                name: "IsAiGenerated",
                table: "Stories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
