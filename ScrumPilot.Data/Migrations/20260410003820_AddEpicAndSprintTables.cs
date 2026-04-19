using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEpicAndSprintTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SprintId",
                table: "Stories",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "EpicId",
                table: "Stories",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateTable(
                name: "Epic",
                columns: table => new
                {
                    EpicId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Epic", x => x.EpicId);
                });

            migrationBuilder.CreateTable(
                name: "Sprint",
                columns: table => new
                {
                    SprintId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SprintGoal = table.Column<string>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateClosed = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sprint", x => x.SprintId);
                });

            migrationBuilder.InsertData(
                table: "Epic",
                columns: new[] { "EpicId", "Name", "DateCreated" },
                values: new object[] { 1, "Scrum Board Filtering", new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "Sprint",
                columns: new[] { "SprintId", "SprintGoal", "StartDate", "EndDate", "IsOpen", "DateClosed" },
                values: new object[] { 1, "Enable scrum board story filtering by sprint and epic", new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc), true, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Epic",
                keyColumn: "EpicId",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Sprint",
                keyColumn: "SprintId",
                keyValue: 1);

            migrationBuilder.DropTable(
                name: "Epic");

            migrationBuilder.DropTable(
                name: "Sprint");

            migrationBuilder.AlterColumn<int>(
                name: "SprintId",
                table: "Stories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "EpicId",
                table: "Stories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
