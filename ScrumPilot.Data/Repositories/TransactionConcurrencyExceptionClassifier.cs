using Microsoft.Data.SqlClient;

namespace ScrumPilot.Data.Repositories;

public static class TransactionConcurrencyExceptionClassifier
{
    public static bool IsConcurrencyConflict(Exception exception) =>
        Find(exception)?.Number is 1205 or 1222 or 3960 or 41302 or 41305 or 41325;

    internal static SqlException? Find(Exception? exception)
    {
        while (exception != null)
        {
            if (exception is SqlException sql) return sql;
            exception = exception.InnerException;
        }
        return null;
    }

    internal static bool IsUniqueViolation(Exception exception, string constraint) =>
        Find(exception) is { Number: 2601 or 2627 } sql
        && sql.Message.Contains(constraint, StringComparison.Ordinal);
}
