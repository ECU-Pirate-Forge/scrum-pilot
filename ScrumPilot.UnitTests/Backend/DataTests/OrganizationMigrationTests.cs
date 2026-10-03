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
        await using var connection = new SqliteConnection("Data Source=:memory:");
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
        Assert.Equal("Member", await ScalarAsync<string>(
            connection,
            """SELECT "Role" FROM "OrganizationMemberships" WHERE "UserId" = 'developer-user';"""));
        Assert.Equal(2, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "ProjectMemberships" WHERE "UserId" = 'developer-user';"""));
        Assert.Equal(0, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM "ProjectMemberships" WHERE "UserId" = 'admin-user';"""));
        Assert.Equal(2, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "Project";"""));
        Assert.Equal(2, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "AspNetUsers";"""));
        Assert.Equal(1, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "UserDashboardPreferences";"""));
        Assert.Equal(0, await ScalarAsync<long>(
            connection,
            """
            SELECT COUNT(*)
            FROM "AspNetUsers"
            WHERE "DefaultOrganizationId" IS NULL
               OR "DefaultOrganizationId" <> (SELECT "OrganizationId" FROM "Organizations" WHERE "NormalizedName" = 'PIRATE FORGE');
            """));

        await migrator.MigrateAsync(InitialMigration);

        Assert.Equal(2, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "Project";"""));
        Assert.Equal(2, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "AspNetUsers";"""));
        Assert.Equal(1, await ScalarAsync<long>(connection, """SELECT COUNT(*) FROM "UserDashboardPreferences";"""));
        Assert.Equal(0, await ScalarAsync<long>(
            connection,
            """SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Organizations';"""));
    }

    [Fact]
    public async Task Migration_SucceedsWhenThereAreNoUsersOrProjects()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
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
            VALUES ('developer-user', 101, '{{"layout":"existing"}}');
            """);
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
