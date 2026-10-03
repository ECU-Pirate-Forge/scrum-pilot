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

    [Fact]
    public async Task RevokeAsync_AfterAcceptanceCommits_ReportsConflictWithoutOverwritingAccepted()
    {
        await using var database = await InvitationRaceDatabase.CreateAsync();
        await using var revokeContext = database.CreateContext();
        await using var acceptContext = database.CreateContext();
        await revokeContext.OrganizationInvitations.SingleAsync();

        var acceptance = await new OrganizationInvitationRepository(acceptContext).AcceptAsync(
            database.TokenHash,
            "member",
            "MEMBER@EXAMPLE.COM",
            database.UtcNow);
        var revoked = await new OrganizationInvitationRepository(revokeContext).RevokeAsync(
            database.OrganizationId,
            database.InvitationId);

        await using var assertionContext = database.CreateContext();
        var invitation = await assertionContext.OrganizationInvitations.SingleAsync();
        Assert.Equal(InvitationAcceptanceStatus.Success, acceptance.Status);
        Assert.False(revoked);
        Assert.Equal(OrganizationInvitationStatus.Accepted, invitation.Status);
        Assert.Equal(database.UtcNow, invitation.AcceptedAt);
        Assert.True(await assertionContext.OrganizationMemberships.AnyAsync(x =>
            x.OrganizationId == database.OrganizationId && x.UserId == "member"));
    }

    [Fact]
    public async Task AcceptAsync_AfterRevokeCommits_FailsWithoutCreatingMembership()
    {
        await using var database = await InvitationRaceDatabase.CreateAsync();
        await using var revokeContext = database.CreateContext();
        await using var acceptContext = database.CreateContext();

        var revoked = await new OrganizationInvitationRepository(revokeContext).RevokeAsync(
            database.OrganizationId,
            database.InvitationId);
        var acceptance = await new OrganizationInvitationRepository(acceptContext).AcceptAsync(
            database.TokenHash,
            "member",
            "MEMBER@EXAMPLE.COM",
            database.UtcNow);

        await using var assertionContext = database.CreateContext();
        Assert.True(revoked);
        Assert.Equal(InvitationAcceptanceStatus.Invalid, acceptance.Status);
        Assert.Equal(
            OrganizationInvitationStatus.Revoked,
            (await assertionContext.OrganizationInvitations.SingleAsync()).Status);
        Assert.False(await assertionContext.OrganizationMemberships.AnyAsync(x =>
            x.OrganizationId == database.OrganizationId && x.UserId == "member"));
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

    private sealed class InvitationRaceDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _keeper;
        private readonly DbContextOptions<ScrumPilotContext> _options;

        private InvitationRaceDatabase(
            SqliteConnection keeper,
            DbContextOptions<ScrumPilotContext> options,
            int organizationId,
            int invitationId,
            DateTime utcNow,
            string tokenHash)
        {
            _keeper = keeper;
            _options = options;
            OrganizationId = organizationId;
            InvitationId = invitationId;
            UtcNow = utcNow;
            TokenHash = tokenHash;
        }

        public int OrganizationId { get; }
        public int InvitationId { get; }
        public DateTime UtcNow { get; }
        public string TokenHash { get; }

        public static async Task<InvitationRaceDatabase> CreateAsync()
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"invitation-race-{Guid.NewGuid():N}",
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared
            }.ToString();
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            var options = new DbContextOptionsBuilder<ScrumPilotContext>()
                .UseSqlite(connectionString)
                .Options;
            await using var context = new ScrumPilotContext(options);
            await context.Database.EnsureCreatedAsync();
            var utcNow = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);
            const string tokenHash =
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            context.Users.AddRange(
                new ApplicationUser
                {
                    Id = "owner",
                    UserName = "owner",
                    NormalizedUserName = "OWNER"
                },
                new ApplicationUser
                {
                    Id = "member",
                    UserName = "member",
                    NormalizedUserName = "MEMBER"
                });
            var organization = new Organization
            {
                Name = "Pirate Forge",
                NormalizedName = "PIRATE FORGE",
                CreatedAt = utcNow
            };
            context.Organizations.Add(organization);
            await context.SaveChangesAsync();
            var invitation = new OrganizationInvitation
            {
                OrganizationId = organization.OrganizationId,
                Email = "member@example.com",
                NormalizedEmail = "MEMBER@EXAMPLE.COM",
                TokenHash = tokenHash,
                InvitedByUserId = "owner",
                Role = OrganizationRole.Member,
                Status = OrganizationInvitationStatus.Pending,
                CreatedAt = utcNow.AddHours(-1),
                ExpiresAt = utcNow.AddHours(1)
            };
            context.OrganizationInvitations.Add(invitation);
            await context.SaveChangesAsync();
            return new(
                keeper,
                options,
                organization.OrganizationId,
                invitation.OrganizationInvitationId,
                utcNow,
                tokenHash);
        }

        public ScrumPilotContext CreateContext() => new(_options);

        public ValueTask DisposeAsync() => _keeper.DisposeAsync();
    }
}
