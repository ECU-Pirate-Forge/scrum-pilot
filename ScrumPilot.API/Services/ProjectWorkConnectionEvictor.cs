using Microsoft.AspNetCore.SignalR;
using ScrumPilot.API.Hubs;

namespace ScrumPilot.API.Services;

public sealed class ProjectWorkConnectionEvictor(IHubContext<ProjectWorkHub> hub, ProjectWorkSubscriptions subscriptions,
    ILogger<ProjectWorkConnectionEvictor> logger)
{
    public async Task EvictAsync(int? projectId = null, int? organizationId = null, string? userId = null,
        CancellationToken ct = default)
    {
        foreach (var connection in subscriptions.Connections.Where(pair =>
                     (!projectId.HasValue || pair.Value.ProjectId == projectId)
                     && (!organizationId.HasValue || pair.Value.OrganizationId == organizationId)
                     && (userId is null || pair.Value.UserId == userId)))
        {
            if (subscriptions.Connections.TryRemove(connection.Key, out _))
            {
                try
                {
                    await hub.Clients.Client(connection.Key).SendAsync("ProjectAccessRevoked", connection.Value.ProjectId, ct);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException)
                {
                    logger.LogWarning(ex, "Project access revocation notification failed for project {ProjectId}", connection.Value.ProjectId);
                }
            }
        }
    }
}
