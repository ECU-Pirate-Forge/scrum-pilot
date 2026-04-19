using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNavigationRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Stories_EpicId",
                table: "Stories",
                column: "EpicId");

            migrationBuilder.CreateIndex(
                name: "IX_Stories_SprintId",
                table: "Stories",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_Comment_PbiId",
                table: "Comment",
                column: "PbiId");

            migrationBuilder.AddForeignKey(
                name: "FK_Comment_Stories_PbiId",
                table: "Comment",
                column: "PbiId",
                principalTable: "Stories",
                principalColumn: "PbiId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_Epic_EpicId",
                table: "Stories",
                column: "EpicId",
                principalTable: "Epic",
                principalColumn: "EpicId");

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_Sprint_SprintId",
                table: "Stories",
                column: "SprintId",
                principalTable: "Sprint",
                principalColumn: "SprintId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comment_Stories_PbiId",
                table: "Comment");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_Epic_EpicId",
                table: "Stories");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_Sprint_SprintId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Stories_EpicId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Stories_SprintId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Comment_PbiId",
                table: "Comment");
        }
    }
}
