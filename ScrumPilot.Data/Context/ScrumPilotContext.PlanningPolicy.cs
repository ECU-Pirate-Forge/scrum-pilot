using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace ScrumPilot.Data.Context;

public partial class ScrumPilotContext
{
    public async Task AcquirePlanningPolicyLockAsync(int projectId, CancellationToken ct)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Planning policy validation requires an enclosing transaction.");
        try
        {
            await Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProjectGraphStates" ("ProjectId", "Revision") VALUES ({projectId}, 0)
            ON CONFLICT ("ProjectId") DO UPDATE SET "Revision" = "ProjectGraphStates"."Revision"
            """, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        { throw new DbUpdateConcurrencyException("Planning policy changed; reload and retry.", ex); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        { throw new DbUpdateConcurrencyException("Planning policy is being updated; retry after refreshing.", ex); }
    }
}
