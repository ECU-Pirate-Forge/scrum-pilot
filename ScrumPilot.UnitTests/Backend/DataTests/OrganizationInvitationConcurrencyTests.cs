using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ScrumPilot.Data.Repositories;

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
}
