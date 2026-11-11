using ScrumPilot.Data.Tenancy;

namespace ScrumPilot.API.Authorization;

/// <summary>SignalR authorization uses a fresh scope and current central membership on every call.</summary>
internal sealed class InvocationOrganizationAccessService(IServiceScopeFactory scopes, int organizationId, string userId)
    : IOrganizationAccessService
{
    private async Task<T> Run<T>(Func<IOrganizationAccessService, Task<T>> action)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantSelection>().OrganizationId = organizationId;
        try
        {
            var current = scope.ServiceProvider.GetRequiredService<IOrganizationAccessService>();
            if (!await current.IsOrganizationMemberAsync(userId, organizationId)) return default!;
            return await action(current);
        }
        catch (TenantUnavailableException) { return default!; }
    }
    public Task<bool> IsOrganizationMemberAsync(string u, int o, CancellationToken c = default) =>
        Run(s => s.IsOrganizationMemberAsync(u, o, c));
    public Task<bool> IsOrganizationOwnerAsync(string u, int o, CancellationToken c = default) =>
        Run(s => s.IsOrganizationOwnerAsync(u, o, c));
    public Task<bool> CanAccessProjectAsync(string u, int p, CancellationToken c = default) =>
        Run(s => s.CanAccessProjectAsync(u, p, c));
    public Task<int?> GetOrganizationIdForProjectAsync(int p, CancellationToken c = default) =>
        Run(s => s.GetOrganizationIdForProjectAsync(p, c));
    public Task<int?> GetOrganizationIdForPbiAsync(int p, CancellationToken c = default) =>
        Run(s => s.GetOrganizationIdForPbiAsync(p, c));
    public Task<int?> GetOrganizationIdForSprintAsync(int p, CancellationToken c = default) =>
        Run(s => s.GetOrganizationIdForSprintAsync(p, c));
    public Task<int?> GetOrganizationIdForEpicAsync(int p, CancellationToken c = default) =>
        Run(s => s.GetOrganizationIdForEpicAsync(p, c));
    public Task<bool> SprintBelongsToProjectAsync(int id, int p, CancellationToken c = default) =>
        Run(s => s.SprintBelongsToProjectAsync(id, p, c));
    public Task<bool> EpicBelongsToProjectAsync(int id, int p, CancellationToken c = default) =>
        Run(s => s.EpicBelongsToProjectAsync(id, p, c));
    public Task<bool> PbiBelongsToProjectAsync(int id, int p, CancellationToken c = default) =>
        Run(s => s.PbiBelongsToProjectAsync(id, p, c));
    public Task<bool> UserCanBeAssignedToProjectAsync(string u, int p, CancellationToken c = default) =>
        Run(s => s.UserCanBeAssignedToProjectAsync(u, p, c));
}
