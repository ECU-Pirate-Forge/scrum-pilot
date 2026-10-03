using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ScrumPilot.Data.Context;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class OrganizationMigrationTests
{
    private const string InitialMigration = "20260422233327_InitialCreate";

    [Fact]
    public async Task Migration_BackfillsExistingUsersProjectsAndPreservesRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(InitialMigration);
        await SeedExistingDataAsync(context);

        await migrator.MigrateAsync();

        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "Organizations" WHERE "Name" = 'Pirate Forge' AND "NormalizedName" = 'PIRATE FORGE' AND length("RowVersion") > 0;"""));
        Assert.Equal(2, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "Project" WHERE "OrganizationId" = (SELECT "OrganizationId" FROM "Organizations" WHERE "NormalizedName" = 'PIRATE FORGE');"""));
        Assert.Equal("Owner", await ScalarAsync<string>(
            connection,
            """SELECT "Role" FROM "OrganizationMemberships" WHERE "UserId" = 'admin-user';"""));
        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "OrganizationMemberships" WHERE "UserId" = 'admin-user' AND "Role" = 'Owner';"""));
        Assert.Equal("Member", await ScalarAsync<string>(
            connection,
            """SELECT "Role" FROM "OrganizationMemberships" WHERE "UserId" = 'developer-user';"""));
        Assert.Equal(2, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "ProjectMemberships" WHERE "UserId" = 'developer-user';"""));
        Assert.Equal(0, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "ProjectMemberships" WHERE "UserId" = 'admin-user';"""));
        Assert.Equal(2, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "ProjectMemberships" WHERE "UserId" = 'developer-user' AND "GrantedByUserId" = 'admin-user';"""));
        Assert.Equal(2, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "Project";"""));
        Assert.Equal(2, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "AspNetUsers";"""));
        Assert.Equal(1, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "UserDashboardPreferences";"""));
        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT json_valid("PreferencesJson") FROM "UserDashboardPreferences";"""));
        Assert.Equal(0, await ScalarAsync<long>(
            connection,
            """
            SELECT COUNT(*)
            FROM "AspNetUsers"
            WHERE "DefaultOrganizationId" IS NULL
               OR "DefaultOrganizationId" <> (SELECT "OrganizationId" FROM "Organizations" WHERE "NormalizedName" = 'PIRATE FORGE');
            """));
        await AssertTimestampBackfillAsync(context);
        await AssertChildGraphAsync(connection);

        await migrator.MigrateAsync(InitialMigration);

        Assert.Equal(2, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "Project";"""));
        Assert.Equal(2, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "AspNetUsers";"""));
        Assert.Equal(1, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "UserDashboardPreferences";"""));
        Assert.Equal(0, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Organizations';"""));
        await AssertChildGraphAsync(connection);
        Assert.Equal(1, await ScalarAsync<long>(connection, "PRAGMA foreign_keys;"));
    }

    [Fact]
    public async Task Migration_CleansLegacyOrphansBeforeAddingForeignKeys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(InitialMigration);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetUsers" (
                "Id", "UiPreference", "DefaultProjectId", "UserName", "NormalizedUserName",
                "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('orphan-default-user', 'Light', 999, 'orphan', 'ORPHAN', 1, 0, 0, 1, 0);

            INSERT INTO "Project" ("ProjectId", "ProjectName", "Description")
            VALUES (101, 'Black Pearl', 'Existing project');

            INSERT INTO "UserDashboardPreferences" ("UserId", "ProjectId", "PreferencesJson")
            VALUES ('orphan-default-user', 999, json_object('layout', 'missing-project')),
                   ('missing-user', 101, json_object('layout', 'missing-user'));
            """);

        await migrator.MigrateAsync();

        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "AspNetUsers" WHERE "Id" = 'orphan-default-user' AND "DefaultProjectId" IS NULL;"""));
        Assert.Equal(0, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "UserDashboardPreferences";"""));
        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """
            SELECT COUNT(*)
            FROM "ProjectMemberships"
            WHERE "ProjectId" = 101
              AND "UserId" = 'orphan-default-user'
              AND "GrantedByUserId" = 'orphan-default-user';
            """));
        Assert.Equal(0, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM pragma_foreign_key_check;"""));
    }

    [Fact]
    public async Task Migration_SucceedsWhenThereAreNoUsersOrProjects()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(InitialMigration);
        await migrator.MigrateAsync();

        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "Organizations" WHERE "NormalizedName" = 'PIRATE FORGE';"""));
        Assert.Equal(0, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "OrganizationMemberships";"""));
        Assert.Equal(0, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "ProjectMemberships";"""));
    }

    private static ScrumPilotContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ScrumPilotContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new ScrumPilotContext(options);
    }

    private static async Task SeedExistingDataAsync(ScrumPilotContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
            VALUES ('admin-role', 'Admin', 'ADMIN', 'admin-role-stamp'),
                   ('developer-role', 'Developer', 'DEVELOPER', 'developer-role-stamp');
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetUsers" (
                "Id", "UiPreference", "DefaultProjectId", "UserName", "NormalizedUserName",
                "Email", "NormalizedEmail", "EmailConfirmed", "PhoneNumberConfirmed",
                "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES
                ('admin-user', 'Light', 101, 'admin', 'ADMIN', 'admin@example.test', 'ADMIN@EXAMPLE.TEST', 1, 0, 0, 1, 0),
                ('developer-user', 'Dark', 102, 'developer', 'DEVELOPER', 'developer@example.test', 'DEVELOPER@EXAMPLE.TEST', 1, 0, 0, 1, 0);
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
            VALUES ('admin-user', 'admin-role'),
                   ('admin-user', 'developer-role'),
                   ('developer-user', 'developer-role');
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Project" ("ProjectId", "ProjectName", "Description")
            VALUES (101, 'Black Pearl', 'Existing project one'),
                   (102, 'Flying Dutchman', 'Existing project two');
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "UserDashboardPreferences" ("UserId", "ProjectId", "PreferencesJson")
            VALUES ('developer-user', 101, json_object('layout', 'existing'));
            """);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Sprint" ("SprintId", "ProjectId", "SprintTitle", "SprintGoal", "StartDate", "EndDate", "IsOpen")
            VALUES (201, 101, 'Sprint One', 'Ship it', '2026-09-01 00:00:00.0000000', '2026-09-14 00:00:00.0000000', 1);

            INSERT INTO "Epic" ("EpicId", "ProjectId", "Name", "DateCreated")
            VALUES (301, 101, 'Treasure Maps', '2026-09-01 00:00:00.0000000');

            INSERT INTO "Stories" (
                "PbiId", "ProjectId", "Type", "EpicId", "SprintId", "Title", "Description",
                "Status", "Priority", "StoryPoints", "Origin", "IsDraft", "IsFlagged",
                "AssignedToUserId", "DateCreated", "LastUpdated")
            VALUES (
                401, 101, 'UserStory', 301, 201, 'Find treasure', 'Follow the map',
                'New', 'Medium', 5, 'Manual', 0, 0,
                'developer-user', '2026-09-02 00:00:00.0000000', '2026-09-02 00:00:00.0000000');

            INSERT INTO "Comment" ("CommentId", "PbiId", "UserId", "Comment", "CreatedDate")
            VALUES (501, 401, 'developer-user', 'Existing comment', '2026-09-03 00:00:00.0000000');

            INSERT INTO "PbiStatusHistory" ("Id", "PbiId", "FromStatus", "ToStatus", "ChangedAt")
            VALUES (601, 401, 'New', 'Active', '2026-09-03 00:00:00.0000000');
            """);
    }

    private static async Task AssertTimestampBackfillAsync(ScrumPilotContext context)
    {
        var expected = new DateTime(2026, 10, 3, 2, 43, 14, DateTimeKind.Unspecified);
        var organization = await context.Organizations.AsNoTracking().SingleAsync();
        var joinedAt = await context.OrganizationMemberships
            .AsNoTracking()
            .Select(membership => membership.JoinedAt)
            .ToListAsync();
        var grantedAt = await context.ProjectMemberships
            .AsNoTracking()
            .Select(membership => membership.GrantedAt)
            .ToListAsync();

        AssertTimestampComponents(expected, organization.CreatedAt);
        Assert.All(joinedAt, actual => AssertTimestampComponents(expected, actual));
        Assert.All(grantedAt, actual => AssertTimestampComponents(expected, actual));
    }

    private static void AssertTimestampComponents(DateTime expected, DateTime actual)
    {
        Assert.Equal(expected.Ticks, actual.Ticks);
        Assert.Equal(
            (expected.Year, expected.Month, expected.Day, expected.Hour, expected.Minute, expected.Second),
            (actual.Year, actual.Month, actual.Day, actual.Hour, actual.Minute, actual.Second));
    }

    private static async Task AssertChildGraphAsync(SqliteConnection connection)
    {
        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "Sprint" WHERE "SprintId" = 201 AND "ProjectId" = 101;"""));
        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "Epic" WHERE "EpicId" = 301 AND "ProjectId" = 101;"""));
        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "Stories" WHERE "PbiId" = 401 AND "ProjectId" = 101 AND "SprintId" = 201 AND "EpicId" = 301;"""));
        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "Comment" WHERE "CommentId" = 501 AND "PbiId" = 401;"""));
        Assert.Equal(1, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "PbiStatusHistory" WHERE "Id" = 601 AND "PbiId" = 401;"""));
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
