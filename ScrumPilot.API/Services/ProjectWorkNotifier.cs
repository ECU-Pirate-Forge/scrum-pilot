using Microsoft.AspNetCore.SignalR;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Hubs;

namespace ScrumPilot.API.Services;

public sealed class ProjectWorkNotifier(IHubContext<ProjectWorkHub> hub,
    ProjectWorkSubscriptions subscriptions, IOrganizationAccessService access,
    ILogger<ProjectWorkNotifier> logger)
{
    public async Task NotifyAsync(int projectId, long revision, CancellationToken ct)
    {
        foreach (var connection in subscriptions.Connections.Where(pair => pair.Value.ProjectId == projectId))
        {
            try
            {
                if (!await access.CanAccessProjectAsync(connection.Value.UserId, projectId, ct))
                {
                    subscriptions.Connections.TryRemove(connection.Key, out _);
                    continue;
                }
                await hub.Clients.Client(connection.Key).SendAsync("ProjectWorkChanged", projectId, revision, ct);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                // Persistence already committed. Revision reconciliation repairs a missed notification.
                logger.LogWarning(ex, "Project work notification failed for project {ProjectId}, revision {Revision}", projectId, revision);
            }
        }
    }
}
