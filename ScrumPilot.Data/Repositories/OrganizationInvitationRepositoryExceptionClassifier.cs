using Microsoft.Data.Sqlite;
using Npgsql;

namespace ScrumPilot.Data.Repositories;

public sealed class OrganizationInvitationConcurrencyException(
    string message,
    Exception innerException) : Exception(message, innerException);

public static class OrganizationInvitationRepositoryExceptionClassifier
{
    private const string MembershipPrimaryKey = "PK_OrganizationMemberships";
    private const string SqliteMembershipColumns =
        "OrganizationMemberships.OrganizationId, OrganizationMemberships.UserId";

    public static bool IsTransactionConcurrency(Exception exception)
        => TransactionConcurrencyExceptionClassifier.IsConcurrencyConflict(exception);

    public static bool IsMembershipUniqueViolation(Exception exception)
    {
        if (Find<PostgresException>(exception) is
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: MembershipPrimaryKey
            })
        {
            return true;
        }

        var sqlite = Find<SqliteException>(exception);
        return sqlite is not null
               && sqlite.SqliteExtendedErrorCode is 1555 or 2067
               && sqlite.Message.Contains(SqliteMembershipColumns, StringComparison.Ordinal);
    }

    private static T? Find<T>(Exception? exception) where T : Exception
    {
        while (exception is not null)
        {
            if (exception is T match)
            {
                return match;
            }
            exception = exception.InnerException;
        }
        return null;
    }
}
