namespace ScrumPilot.Data.Tenancy;

public enum TenantLifecycleOperation { Upgrade, Restore, Delete, Purge, RenameCatalog }

public interface ITenantLifecycleObserver
{
    Task OnDrainedAsync(int organizationId, string storageKey, TenantLifecycleOperation operation, CancellationToken ct);
}
