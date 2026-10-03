using Microsoft.Data.Sqlite;
using Npgsql;

namespace ScrumPilot.Data.Repositories;

public static class TransactionConcurrencyExceptionClassifier
{
    public static bool IsConcurrencyConflict(Exception exception)
    {
        if (Find<PostgresException>(exception) is
            {
                SqlState: PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.DeadlockDetected
            })
        {
            return true;
        }

        return Find<SqliteException>(exception)?.SqliteErrorCode is 5 or 6;
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
