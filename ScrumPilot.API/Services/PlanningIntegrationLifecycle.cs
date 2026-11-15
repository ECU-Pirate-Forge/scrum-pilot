using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Tenancy;

namespace ScrumPilot.API.Services;

public sealed class PlanningIntegrationLifecycle(ControlPlaneContext control, GitHubCheckRecovery recovery,
    ProjectWorkConnectionEvictor work, IPlanningPokerConnectionEvictor poker,
    ILogger<PlanningIntegrationLifecycle> logger) : ITenantLifecycleObserver, IProjectPlanningRetirement
{
    public async Task SuspendInstallationAsync(long installationId, IReadOnlyCollection<long>? repositoryIds, CancellationToken ct)
    {
        var registrations = await control.GitHubRepositoryRegistrations.Where(item =>
            item.InstallationId == installationId && item.State != RepositoryRegistrationState.Retired
            && (repositoryIds == null || repositoryIds.Contains(item.GitHubRepositoryId))).ToListAsync(ct);
        foreach (var registration in registrations)
        {
            registration.State = RepositoryRegistrationState.Suspended;
            registration.RecoveryError = "GitHub App access changed. Freeze merges until known checks are invalidated and an Owner reconciles authority.";
            registration.UpdatedAt = DateTime.UtcNow;
        }
        await control.SaveChangesAsync(ct);
        foreach (var registration in registrations)
            await recovery.InvalidateAsync(registration, "GitHub App access changed. Scope approval is revoked.", ct);
    }

    public async Task OnDrainedAsync(int organizationId, string storageKey, TenantLifecycleOperation operation, CancellationToken ct)
    {
        await work.EvictAsync(organizationId: organizationId, ct: ct);
        await poker.EvictOrganizationAsync(organizationId, ct);
        var registrations = await control.GitHubRepositoryRegistrations.Where(item =>
            item.OrganizationId == organizationId && item.StorageKey == storageKey
            && item.State != RepositoryRegistrationState.Retired).ToListAsync(ct);
        foreach (var registration in registrations)
        {
            registration.State = operation == TenantLifecycleOperation.Purge
                ? RepositoryRegistrationState.Retiring : RepositoryRegistrationState.Suspended;
            registration.RecoveryError = "Repository checks require reconciliation after organization maintenance. Freeze merges until invalidation is confirmed.";
            registration.UpdatedAt = DateTime.UtcNow;
        }
        await control.SaveChangesAsync(ct);
        foreach (var registration in registrations)
        {
            try
            {
                await recovery.InvalidateAsync(registration,
                    "Organization storage is changing. Reconcile repository authority and revalidate task scope before merging.", ct);
                if (operation == TenantLifecycleOperation.Purge)
                    registration.State = RepositoryRegistrationState.Retired;
                registration.RecoveryError = operation == TenantLifecycleOperation.Purge ? null
                    : "Known checks invalidated. An Owner must reconcile repository configuration and revalidate task scope.";
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or OperationCanceledException)
            {
                registration.RecoveryError = "External check invalidation is unconfirmed. Keep merges frozen and retry repository reconciliation.";
                logger.LogError(exception,
                    "GitHub scope recovery is unconfirmed for organization {OrganizationId}, repository {RepositoryId}; merges must remain frozen",
                    organizationId, registration.GitHubRepositoryId);
                if (operation == TenantLifecycleOperation.Purge || ct.IsCancellationRequested)
                {
                    await control.SaveChangesAsync(CancellationToken.None);
                    throw;
                }
            }
            await control.SaveChangesAsync(ct);
        }
    }

    public async Task RetireProjectAsync(int organizationId, int projectId, CancellationToken ct)
    {
        var registrations = await control.GitHubRepositoryRegistrations.Where(item =>
            item.OrganizationId == organizationId && item.ProjectId == projectId
            && item.State != RepositoryRegistrationState.Retired).ToListAsync(ct);
        foreach (var registration in registrations)
        {
            registration.State = RepositoryRegistrationState.Retiring;
            registration.UpdatedAt = DateTime.UtcNow;
        }
        await control.SaveChangesAsync(ct);
        foreach (var registration in registrations)
        {
            await recovery.InvalidateAsync(registration, "This project is being deleted. Task scope approval is revoked.", ct);
            registration.State = RepositoryRegistrationState.Retired;
            registration.RecoveryError = null;
            await control.SaveChangesAsync(ct);
        }
    }
}
