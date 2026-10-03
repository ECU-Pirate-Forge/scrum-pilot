using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class OrganizationInvitationConcurrencyTests
{
    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public void IsTransactionConcurrency_RecognizesPostgresSqlStates(string sqlState)
    {
        var exception = new PostgresException(
            "transaction failed",
            "ERROR",
            "ERROR",
            sqlState);

        Assert.True(
            OrganizationInvitationRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(5, 517)]
    [InlineData(6, 262)]
    public void IsTransactionConcurrency_RecognizesSqliteBusyAndLocked(
        int primaryErrorCode,
        int extendedErrorCode)
    {
        var exception = new DbUpdateException(
            "save failed",
            new SqliteException(
                "database operation failed",
                primaryErrorCode,
                extendedErrorCode));

        Assert.True(
            OrganizationInvitationRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception));
    }

    [Fact]
    public void IsTransactionConcurrency_RejectsUnrelatedSqliteError()
    {
        var exception = new SqliteException(
            "constraint failed",
            19,
            2067);

        Assert.False(
            OrganizationInvitationRepositoryExceptionClassifier
                .IsTransactionConcurrency(exception));
    }

    [Fact]
    public async Task Invite_WhenSqliteWriteIsLocked_ReturnsConflictWithoutSending()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"scrumpilot-invitation-lock-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 0,
            Pooling = false
        }.ToString();

        try
        {
            var organizationId = await CreateDatabaseAsync(connectionString);
            await using var lockConnection = new SqliteConnection(connectionString);
            await lockConnection.OpenAsync();
            await using var lockTransaction = await lockConnection.BeginTransactionAsync();
            await using (var command = lockConnection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)lockTransaction;
                command.CommandText =
                    "UPDATE Organizations SET Name = Name WHERE OrganizationId = $id";
                command.Parameters.AddWithValue("$id", organizationId);
                await command.ExecuteNonQueryAsync();
            }

            await using var mutationConnection = new SqliteConnection(connectionString);
            await mutationConnection.OpenAsync();
            await using (var timeoutCommand = mutationConnection.CreateCommand())
            {
                timeoutCommand.CommandText = "PRAGMA busy_timeout=0";
                await timeoutCommand.ExecuteNonQueryAsync();
            }
            await using var mutationContext = new ScrumPilotContext(
                new DbContextOptionsBuilder<ScrumPilotContext>()
                    .UseSqlite(
                        mutationConnection,
                        options => options.CommandTimeout(1))
                    .Options);
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.UserId.Returns("owner");
            var access = Substitute.For<IOrganizationAccessService>();
            access.IsOrganizationOwnerAsync(
                    "owner",
                    organizationId,
                    Arg.Any<CancellationToken>())
                .Returns(true);
            var sender = Substitute.For<IInvitationEmailSender>();
            var service = new OrganizationInvitationService(
                new OrganizationInvitationRepository(mutationContext),
                currentUser,
                Substitute.For<IInvitationAcceptanceUserLookup>(),
                access,
                sender,
                TimeProvider.System,
                NullLogger<OrganizationInvitationService>.Instance);
            var controller = new OrganizationInvitationController(service)
            {
                ControllerContext = new()
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "owner")],
                            "test"))
                    }
                }
            };

            var result = await controller.Invite(
                organizationId,
                new("member@example.com", OrganizationRole.Member));

            Assert.IsType<ConflictObjectResult>(result.Result);
            await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void IsMembershipUniqueViolation_RecognizesMembershipPostgresConstraint()
    {
        var exception = new DbUpdateException(
            "save failed",
            new PostgresException(
                "duplicate",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.UniqueViolation,
                constraintName: "PK_OrganizationMemberships"));

        Assert.True(
            OrganizationInvitationRepositoryExceptionClassifier
                .IsMembershipUniqueViolation(exception));
    }

    [Fact]
    public void IsMembershipUniqueViolation_RejectsOtherPostgresConstraint()
    {
        var exception = new PostgresException(
            "duplicate",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: "IX_OrganizationInvitations_TokenHash");

        Assert.False(
            OrganizationInvitationRepositoryExceptionClassifier
                .IsMembershipUniqueViolation(exception));
    }

    [Fact]
    public void IsMembershipUniqueViolation_RecognizesExactSqliteMembershipColumns()
    {
        var exception = new SqliteException(
            "UNIQUE constraint failed: OrganizationMemberships.OrganizationId, OrganizationMemberships.UserId",
            19,
            1555);

        Assert.True(
            OrganizationInvitationRepositoryExceptionClassifier
                .IsMembershipUniqueViolation(exception));
    }

    [Fact]
    public void IsMembershipUniqueViolation_RejectsOtherSqliteUniqueViolation()
    {
        var exception = new SqliteException(
            "UNIQUE constraint failed: OrganizationInvitations.TokenHash",
            19,
            2067);

        Assert.False(
            OrganizationInvitationRepositoryExceptionClassifier
                .IsMembershipUniqueViolation(exception));
    }

    private static async Task<int> CreateDatabaseAsync(string connectionString)
    {
        await using var context = new ScrumPilotContext(
            new DbContextOptionsBuilder<ScrumPilotContext>()
                .UseSqlite(connectionString)
                .Options);
        await context.Database.EnsureCreatedAsync();
        context.Users.Add(new ApplicationUser
        {
            Id = "owner",
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.com",
            NormalizedEmail = "OWNER@EXAMPLE.COM",
            EmailConfirmed = true
        });
        var organization = new Organization
        {
            Name = "Pirate Forge",
            NormalizedName = "PIRATE FORGE",
            CreatedAt = DateTime.UtcNow
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        return organization.OrganizationId;
    }
}
