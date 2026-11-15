using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Tenancy;

namespace ScrumPilot.API.Services;

/// <summary>Inference owns no tenant connection or storage lease. Re-entry is authorized against current central state.</summary>
public sealed class PlanningInferenceSession(OrganizationContext tenant, TenantStorage storage,
    ControlPlaneContext control, TenantSelection selection, ICurrentUser user, IOrganizationAccessService access)
{
    public async Task<T> RunAsync<T>(int? projectId, Func<Task<T>> infer, CancellationToken ct)
    {
        var organizationId = selection.OrganizationId ?? throw new TenantUnavailableException();
        var before = await control.Organizations.AsNoTracking().SingleAsync(x => x.OrganizationId == organizationId, ct);
        var membership = await control.OrganizationMemberships.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.UserId == user.UserId)
            .Select(x => (Guid?)x.MembershipInstanceId).SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException();
        if (projectId.HasValue && !await access.CanAccessProjectAsync(user.UserId, projectId.Value, ct))
            throw new KeyNotFoundException();
        if (tenant.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Inference cannot run inside a planning transaction.");
        tenant.ChangeTracker.Clear();
        await tenant.Database.CloseConnectionAsync();
        tenant.ReleaseLease();
        try { return await infer(); }
        finally
        {
            // Cleanup/re-entry must finish even when the inference request was cancelled.
            var current = await control.Organizations.AsNoTracking().SingleAsync(x => x.OrganizationId == organizationId);
            if (!await control.OrganizationMemberships.AsNoTracking().AnyAsync(x =>
                    x.OrganizationId == organizationId && x.UserId == user.UserId && x.MembershipInstanceId == membership))
                throw new KeyNotFoundException();
            if (current.StorageKey != before.StorageKey || current.OperationId != before.OperationId)
                throw new TenantUnavailableException();
            using var verification = storage.Open(current);
            tenant.AdoptLease(verification);
            try
            {
                await tenant.Database.OpenConnectionAsync();
                TenantStorage.Verify(tenant, current);
                if (projectId.HasValue && !await access.CanAccessProjectAsync(user.UserId, projectId.Value))
                    throw new KeyNotFoundException();
            }
            catch
            {
                await tenant.Database.CloseConnectionAsync();
                tenant.ReleaseLease();
                throw;
            }
        }
    }
}
