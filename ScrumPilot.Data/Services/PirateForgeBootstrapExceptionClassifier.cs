using Microsoft.Data.Sqlite;
using Npgsql;
using ScrumPilot.Data.Repositories;

namespace ScrumPilot.Data.Services;

public static class PirateForgeBootstrapExceptionClassifier
{
    public static bool IsRetryable(Exception exception)
    {
        if (TransactionConcurrencyExceptionClassifier.IsConcurrencyConflict(exception))
        {
            return true;
        }

        if (Find<PostgresException>(exception)?.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return true;
        }

        return Find<SqliteException>(exception)?.SqliteExtendedErrorCode is 1555 or 2067;
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
