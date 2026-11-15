using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ScrumPilot.Data.Context;

public sealed partial class OrganizationContext
{
    public async Task AcquirePlanningPolicyLockAsync(int projectId, CancellationToken ct)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Planning policy validation requires an enclosing transaction.");
        try
        {
            await AcquireProjectLockAsync("planning", projectId, ct);
            await Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProjectGraphStates] ([ProjectId], [Revision])
            SELECT {projectId}, 0 WHERE NOT EXISTS (
                SELECT 1 FROM [ProjectGraphStates] WHERE [ProjectId] = {projectId});
            """, ct);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222 or 3960 or 51001)
        { throw new DbUpdateConcurrencyException("Planning policy changed; reload and retry.", ex); }
    }

    private async Task AcquireProjectLockAsync(string purpose, int projectId, CancellationToken ct)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Project locks require an enclosing transaction.");
        // Never derive coordination identity from an HTTP selection, catalog alias, or tracked entity.
        // The metadata row is provisioned with the tenant and immutable in EF and SQL.
        try
        {
            var storageKey = await TenantMetadata.AsNoTracking().Select(x => x.StorageKey).SingleAsync(ct);
            var resource = $"{purpose}:{storageKey}:{projectId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            await Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = {resource},
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction',
                    @LockTimeout = 15000, @DbPrincipal = 'public';
                IF @result < 0 THROW 51001, 'Planning policy is busy; reload and retry.', 1;
                """, ct);
        }
        catch (SqlException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("Planning lock acquisition was canceled.", ex, ct);
        }
    }
}
