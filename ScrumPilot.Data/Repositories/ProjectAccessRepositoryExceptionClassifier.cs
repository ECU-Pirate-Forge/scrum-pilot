using Microsoft.Data.Sqlite;
using Npgsql;

namespace ScrumPilot.Data.Repositories;

public sealed class ProjectAccessConcurrencyException(
    string message,
    Exception innerException) : Exception(message, innerException);

public static class ProjectAccessRepositoryExceptionClassifier
{
    private const string ProjectMembershipPrimaryKey = "PK_ProjectMemberships";
    private const string SqliteProjectMembershipColumns =
        "ProjectMemberships.ProjectId, ProjectMemberships.UserId";

    public static bool IsTransactionConcurrency(Exception exception) =>
        OrganizationInvitationRepositoryExceptionClassifier
            .IsTransactionConcurrency(exception);

    public static bool IsProjectMembershipUniqueViolation(Exception exception)
    {
        if (Find<PostgresException>(exception) is
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: ProjectMembershipPrimaryKey
            })
        {
            return true;
        }

        var sqlite = Find<SqliteException>(exception);
        return sqlite is not null
               && sqlite.SqliteExtendedErrorCode is 1555 or 2067
               && sqlite.Message.Contains(
                   SqliteProjectMembershipColumns,
                   StringComparison.Ordinal);
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
