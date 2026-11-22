using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations.ControlPlane
{
    /// <inheritdoc />
    public partial class AddOrganizationCatalogNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CatalogName",
                table: "Organizations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CatalogRenamePending",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_CatalogName",
                table: "Organizations",
                column: "CatalogName",
                unique: true,
                filter: "[CatalogName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_CatalogName",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CatalogName",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CatalogRenamePending",
                table: "Organizations");
        }
    }
}
