using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationTenancy : Migration
    {
        private const string PostgreSqlMigrationTimestamp = "2026-10-03 02:43:14+00";
        private const string SqliteMigrationTimestamp = "2026-10-03 02:43:14.0000000";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var migrationTimestampSql = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"
                ? $"TIMESTAMPTZ '{PostgreSqlMigrationTimestamp}'"
                : $"'{SqliteMigrationTimestamp}'";

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Project",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultOrganizationId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    OrganizationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.OrganizationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_NormalizedName",
                table: "Organizations",
                column: "NormalizedName",
                unique: true);

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql($$"""
                    INSERT INTO "Organizations" ("Name", "NormalizedName", "CreatedAt", "RowVersion")
                    VALUES ('Pirate Forge', 'PIRATE FORGE', {{migrationTimestampSql}}, decode('00000000000000000000000000000001', 'hex'));
                    """);
            }
            else
            {
                migrationBuilder.Sql($$"""
                    INSERT INTO "Organizations" ("Name", "NormalizedName", "CreatedAt", "RowVersion")
                    VALUES ('Pirate Forge', 'PIRATE FORGE', {{migrationTimestampSql}}, X'00000000000000000000000000000001');
                    """);
            }

            migrationBuilder.Sql("""
                UPDATE "Project"
                SET "OrganizationId" = (
                    SELECT "OrganizationId"
                    FROM "Organizations"
                    WHERE "NormalizedName" = 'PIRATE FORGE'
                );

                UPDATE "AspNetUsers"
                SET "DefaultOrganizationId" = (
                    SELECT "OrganizationId"
                    FROM "Organizations"
                    WHERE "NormalizedName" = 'PIRATE FORGE'
                );
                """);

            migrationBuilder.CreateTable(
                name: "OrganizationMemberships",
                columns: table => new
                {
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberships", x => new { x.OrganizationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql($$"""
                INSERT INTO "OrganizationMemberships" ("OrganizationId", "UserId", "Role", "JoinedAt")
                SELECT organization."OrganizationId",
                       users."Id",
                       CASE WHEN EXISTS (
                           SELECT 1
                           FROM "AspNetUserRoles" user_roles
                           INNER JOIN "AspNetRoles" roles ON roles."Id" = user_roles."RoleId"
                           WHERE user_roles."UserId" = users."Id"
                             AND roles."NormalizedName" = 'ADMIN'
                       ) THEN 'Owner' ELSE 'Member' END,
                       {{migrationTimestampSql}}
                FROM "AspNetUsers" users
                CROSS JOIN "Organizations" organization
                WHERE organization."NormalizedName" = 'PIRATE FORGE';
                """);

            migrationBuilder.Sql("""
                CREATE TEMP TABLE "__OrganizationBackfillValidation" (
                    "Valid" INTEGER NOT NULL CHECK ("Valid" = 1)
                );
                INSERT INTO "__OrganizationBackfillValidation" ("Valid")
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM "Project" project
                    LEFT JOIN "Organizations" organization
                        ON organization."OrganizationId" = project."OrganizationId"
                    WHERE project."OrganizationId" IS NULL
                       OR organization."OrganizationId" IS NULL
                ) THEN 0 ELSE 1 END;
                DROP TABLE "__OrganizationBackfillValidation";
                """);

            migrationBuilder.AlterColumn<int>(
                name: "OrganizationId",
                table: "Project",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Project_OrganizationId",
                table: "Project",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DefaultOrganizationId",
                table: "AspNetUsers",
                column: "DefaultOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DefaultProjectId",
                table: "AspNetUsers",
                column: "DefaultProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_UserId",
                table: "OrganizationMemberships",
                column: "UserId");

            migrationBuilder.Sql("""
                UPDATE "AspNetUsers"
                SET "DefaultProjectId" = NULL
                WHERE "DefaultProjectId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "Project" project
                      WHERE project."ProjectId" = "AspNetUsers"."DefaultProjectId"
                  );

                DELETE FROM "UserDashboardPreferences"
                WHERE NOT EXISTS (
                          SELECT 1
                          FROM "Project" project
                          WHERE project."ProjectId" = "UserDashboardPreferences"."ProjectId"
                      )
                   OR NOT EXISTS (
                          SELECT 1
                          FROM "AspNetUsers" users
                          WHERE users."Id" = "UserDashboardPreferences"."UserId"
                      );
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Project_Organizations_OrganizationId",
                table: "Project",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Organizations_DefaultOrganizationId",
                table: "AspNetUsers",
                column: "DefaultOrganizationId",
                principalTable: "Organizations",
                principalColumn: "OrganizationId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Project_DefaultProjectId",
                table: "AspNetUsers",
                column: "DefaultProjectId",
                principalTable: "Project",
                principalColumn: "ProjectId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "IX_UserDashboardPreferences_ProjectId",
                table: "UserDashboardPreferences",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserDashboardPreferences_AspNetUsers_UserId",
                table: "UserDashboardPreferences",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserDashboardPreferences_Project_ProjectId",
                table: "UserDashboardPreferences",
                column: "ProjectId",
                principalTable: "Project",
                principalColumn: "ProjectId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.CreateTable(
                name: "ProjectMemberships",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GrantedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMemberships", x => new { x.ProjectId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ProjectMemberships_AspNetUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectMemberships_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql($$"""
                INSERT INTO "ProjectMemberships" ("ProjectId", "UserId", "GrantedAt", "GrantedByUserId")
                SELECT project."ProjectId",
                       membership."UserId",
                       {{migrationTimestampSql}},
                       COALESCE((
                           SELECT owner."UserId"
                           FROM "OrganizationMemberships" owner
                           WHERE owner."OrganizationId" = membership."OrganizationId"
                             AND owner."Role" = 'Owner'
                           ORDER BY owner."UserId"
                           LIMIT 1
                       ), membership."UserId")
                FROM "OrganizationMemberships" membership
                INNER JOIN "Project" project
                    ON project."OrganizationId" = membership."OrganizationId"
                WHERE membership."Role" = 'Member';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMemberships_GrantedByUserId",
                table: "ProjectMemberships",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMemberships_UserId",
                table: "ProjectMemberships",
                column: "UserId");

            migrationBuilder.CreateTable(
                name: "OrganizationInvitations",
                columns: table => new
                {
                    OrganizationInvitationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvitedByUserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveryError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInvitations", x => x.OrganizationInvitationId);
                    table.ForeignKey(
                        name: "FK_OrganizationInvitations_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationInvitations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "OrganizationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_ExpiresAt",
                table: "OrganizationInvitations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_InvitedByUserId",
                table: "OrganizationInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_OrganizationId_NormalizedEmail_Stat~",
                table: "OrganizationInvitations",
                columns: new[] { "OrganizationId", "NormalizedEmail", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_TokenHash",
                table: "OrganizationInvitations",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);
            }

            migrationBuilder.DropTable(name: "OrganizationInvitations");
            migrationBuilder.DropTable(name: "OrganizationMemberships");
            migrationBuilder.DropTable(name: "ProjectMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Organizations_DefaultOrganizationId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Project_DefaultProjectId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Project_Organizations_OrganizationId",
                table: "Project");

            migrationBuilder.DropForeignKey(
                name: "FK_UserDashboardPreferences_AspNetUsers_UserId",
                table: "UserDashboardPreferences");

            migrationBuilder.DropForeignKey(
                name: "FK_UserDashboardPreferences_Project_ProjectId",
                table: "UserDashboardPreferences");

            migrationBuilder.DropIndex(
                name: "IX_UserDashboardPreferences_ProjectId",
                table: "UserDashboardPreferences");

            migrationBuilder.DropIndex(
                name: "IX_Project_OrganizationId",
                table: "Project");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DefaultOrganizationId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DefaultProjectId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "DefaultOrganizationId",
                table: "AspNetUsers");

            migrationBuilder.Sql("""DROP TABLE "Organizations";""");

            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
            }
        }
    }
}
