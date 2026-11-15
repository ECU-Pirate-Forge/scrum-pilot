using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScrumPilot.Data.Migrations.ControlPlane
{
    /// <inheritdoc />
    public partial class AddPlanningIntegrationRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHubRepositoryRegistrations",
                columns: table => new
                {
                    GitHubRepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ProjectRepositoryId = table.Column<int>(type: "int", nullable: false),
                    InstallationId = table.Column<long>(type: "bigint", nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    RecoveryError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubRepositoryRegistrations", x => x.GitHubRepositoryId);
                    table.UniqueConstraint("AK_GitHubRepositoryRegistrations_RegistrationId", x => x.RegistrationId);
                });

            migrationBuilder.CreateTable(
                name: "GitHubWebhookDeliveries",
                columns: table => new
                {
                    DeliveryId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubWebhookDeliveries", x => x.DeliveryId);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestScopeAttempts",
                columns: table => new
                {
                    PullRequestScopeAttemptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ProjectRepositoryId = table.Column<int>(type: "int", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "int", nullable: false),
                    HeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestScopeAttempts", x => x.PullRequestScopeAttemptId);
                    table.ForeignKey(
                        name: "FK_PullRequestScopeAttempts_GitHubRepositoryRegistrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "GitHubRepositoryRegistrations",
                        principalColumn: "RegistrationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubRepositoryRegistrations_OrganizationId_StorageKey_ProjectId",
                table: "GitHubRepositoryRegistrations",
                columns: new[] { "OrganizationId", "StorageKey", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScopeAttempts_RegistrationId_PullRequestNumber",
                table: "PullRequestScopeAttempts",
                columns: new[] { "RegistrationId", "PullRequestNumber" });
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_PullRequestScopeAttempts_AppendOnly] ON [PullRequestScopeAttempts]
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 51002, 'Scope recovery heads are append-only.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_GitHubRepositoryRegistrations_Authority] ON [GitHubRepositoryRegistrations]
                AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT [GitHubRepositoryId], [RegistrationId], [OrganizationId], [StorageKey], [ProjectId] FROM deleted
                        EXCEPT
                        SELECT [GitHubRepositoryId], [RegistrationId], [OrganizationId], [StorageKey], [ProjectId] FROM inserted)
                        THROW 51003, 'Repository authority cannot be reassigned or removed.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_PullRequestScopeAttempts_AppendOnly];");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_GitHubRepositoryRegistrations_Authority];");
            migrationBuilder.DropTable(
                name: "GitHubWebhookDeliveries");

            migrationBuilder.DropTable(
                name: "PullRequestScopeAttempts");

            migrationBuilder.DropTable(
                name: "GitHubRepositoryRegistrations");
        }
    }
}
