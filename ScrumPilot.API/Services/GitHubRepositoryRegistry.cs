using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using Microsoft.Extensions.Options;

namespace ScrumPilot.API.Services;

public sealed class GitHubRepositoryRegistry(ControlPlaneContext control, TenantSelection selection,
    IOptions<FeatureOptions> features, ILogger<GitHubRepositoryRegistry>? logger = null)
{
    public bool IntegrationEnabled => features.Value.GitHubIntegration;

    public Task<bool> IsValidationCurrentAsync(ProjectRepository repository, PullRequestScopeCheck check, CancellationToken ct) =>
        check.RepositoryVersion == repository.Version ? IsCurrentAsync(repository, ct) : Task.FromResult(false);

    public async Task<bool> IsCurrentAsync(ProjectRepository repository, CancellationToken ct)
    {
        if (!IntegrationEnabled || selection.OrganizationId is not int organizationId || repository.RegistrationId == Guid.Empty)
            return false;
        return await (from registration in control.GitHubRepositoryRegistrations.AsNoTracking()
                      join organization in control.Organizations.AsNoTracking()
                          on registration.OrganizationId equals organization.OrganizationId
                      where registration.GitHubRepositoryId == repository.GitHubRepositoryId
                          && registration.RegistrationId == repository.RegistrationId
                          && registration.OrganizationId == organizationId
                          && registration.StorageKey == organization.StorageKey
                          && registration.ProjectId == repository.ProjectId
                          && registration.ProjectRepositoryId == repository.ProjectRepositoryId
                          && registration.ConfigurationVersion == repository.Version
                          && registration.InstallationId == repository.InstallationId
                          && registration.State == RepositoryRegistrationState.Active
                          && organization.StorageState == "Active" && organization.DeletedAt == null
                          && !organization.RestorePending
                      select registration.GitHubRepositoryId).AnyAsync(ct);
    }

    public async Task<GitHubRepositoryRegistration> RequireCurrentAsync(ProjectRepository repository, CancellationToken ct)
    {
        if (!await IsCurrentAsync(repository, ct))
            throw new InvalidOperationException("Repository policy is suspended or changed. An Owner must reconcile its configuration and scope checks.");
        return await control.GitHubRepositoryRegistrations.AsNoTracking()
            .SingleAsync(item => item.GitHubRepositoryId == repository.GitHubRepositoryId, ct);
    }

    public async Task SuspendWhileDisabledAsync(ProjectRepository repository, CancellationToken ct)
    {
        if (IntegrationEnabled) throw new InvalidOperationException("GitHub integration is enabled.");
        var owned = await RequireOwnedAsync(repository, ct);
        var registration = await control.GitHubRepositoryRegistrations.SingleAsync(
            item => item.RegistrationId == owned.RegistrationId, ct);
        if (registration.State is not (RepositoryRegistrationState.Retiring or RepositoryRegistrationState.Retired))
            registration.State = RepositoryRegistrationState.Suspended;
        registration.RecoveryError = "GitHub integration is disabled; external checks are not invalidated. Freeze merges and reconcile repository authority before re-enabling.";
        registration.UpdatedAt = DateTime.UtcNow;
        await control.SaveChangesAsync(ct);
        logger?.LogWarning("Repository {RepositoryId} was fenced while GitHub integration is disabled; external checks require reconciliation",
            repository.GitHubRepositoryId);
    }

    public async Task<GitHubRepositoryRegistration> RequireOwnedAsync(ProjectRepository repository, CancellationToken ct) =>
        await control.GitHubRepositoryRegistrations.AsNoTracking().SingleOrDefaultAsync(item =>
            item.RegistrationId == repository.RegistrationId && item.GitHubRepositoryId == repository.GitHubRepositoryId
            && item.OrganizationId == selection.OrganizationId && item.ProjectId == repository.ProjectId, ct)
        ?? throw new InvalidOperationException("Repository authority is not registered for this organization and project.");

    public async Task RetireAsync(ProjectRepository repository, CancellationToken ct)
    {
        var registration = await control.GitHubRepositoryRegistrations.SingleAsync(item =>
            item.RegistrationId == repository.RegistrationId && item.OrganizationId == selection.OrganizationId
            && item.ProjectId == repository.ProjectId, ct);
        registration.State = RepositoryRegistrationState.Retired;
        registration.UpdatedAt = DateTime.UtcNow;
        await control.SaveChangesAsync(ct);
    }

    public async Task<GitHubRepositoryRegistration> ResolveAsync(long repositoryId, long installationId, CancellationToken ct)
    {
        var registration = await control.GitHubRepositoryRegistrations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GitHubRepositoryId == repositoryId
                && item.InstallationId == installationId && item.State == RepositoryRegistrationState.Active, ct)
            ?? throw new KeyNotFoundException("Repository registration not found.");
        if (!await control.Organizations.AsNoTracking().AnyAsync(item => item.OrganizationId == registration.OrganizationId
            && item.StorageKey == registration.StorageKey && item.DeletedAt == null && !item.RestorePending
            && item.StorageState == "Active", ct))
            throw new TenantUnavailableException();
        return registration;
    }

    public async Task<GitHubRepositoryRegistration> ReserveAsync(int projectId, ConfigureProjectRepositoryRequest request,
        CancellationToken ct)
    {
        var organizationId = selection.OrganizationId ?? throw new TenantUnavailableException();
        var organization = await control.Organizations.AsNoTracking().SingleAsync(item => item.OrganizationId == organizationId, ct);
        if (organization.DeletedAt != null || organization.RestorePending || organization.StorageState != "Active")
            throw new TenantUnavailableException();
        var registration = await control.GitHubRepositoryRegistrations.SingleOrDefaultAsync(
            item => item.GitHubRepositoryId == request.GitHubRepositoryId, ct);
        if (registration is not null && (registration.OrganizationId != organizationId
            || registration.StorageKey != organization.StorageKey || registration.ProjectId != projectId))
            throw new InvalidOperationException("This repository is reserved by another project's required-check policy.");
        if (registration?.State is RepositoryRegistrationState.Retiring or RepositoryRegistrationState.Retired)
            throw new InvalidOperationException("This repository policy is retired. Resolve its external checks before re-registering.");
        if (registration is null)
        {
            registration = new()
            {
                GitHubRepositoryId = request.GitHubRepositoryId,
                OrganizationId = organizationId,
                StorageKey = organization.StorageKey,
                ProjectId = projectId,
                Owner = request.Owner,
                Name = request.Name
            };
            control.GitHubRepositoryRegistrations.Add(registration);
        }
        registration.State = RepositoryRegistrationState.Pending;
        registration.InstallationId = request.InstallationId;
        registration.Owner = request.Owner;
        registration.Name = request.Name;
        registration.UpdatedAt = DateTime.UtcNow;
        await control.SaveChangesAsync(ct);
        return registration;
    }

    public async Task ActivateAsync(GitHubRepositoryRegistration registration, ProjectRepository repository, CancellationToken ct)
    {
        if (registration.State != RepositoryRegistrationState.Pending || registration.RegistrationId != repository.RegistrationId)
            throw new InvalidOperationException("The repository reservation changed. Reload and retry configuration.");
        registration.ProjectRepositoryId = repository.ProjectRepositoryId;
        registration.ConfigurationVersion = repository.Version;
        registration.InstallationId = repository.InstallationId;
        registration.Owner = repository.Owner;
        registration.Name = repository.Name;
        registration.State = RepositoryRegistrationState.Active;
        registration.RecoveryError = null;
        registration.UpdatedAt = DateTime.UtcNow;
        await control.SaveChangesAsync(ct);
    }
}
