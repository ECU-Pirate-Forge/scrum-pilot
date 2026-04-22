using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPbiIssueLinkAndDependency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IssueLink",
                table: "Stories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DependsOnPbiId",
                table: "Stories",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssueLink",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "DependsOnPbiId",
                table: "Stories");
        }
    }
}
