using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations.ControlPlane
{
    /// <inheritdoc />
    public partial class AddInvitationDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultOrganizationId",
                table: "OrganizationInvitations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultProjectId",
                table: "OrganizationInvitations",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultOrganizationId",
                table: "OrganizationInvitations");

            migrationBuilder.DropColumn(
                name: "DefaultProjectId",
                table: "OrganizationInvitations");
        }
    }
}
