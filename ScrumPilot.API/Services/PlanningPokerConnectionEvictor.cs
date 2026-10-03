using Microsoft.AspNetCore.SignalR;
using ScrumPilot.API.Hubs;

namespace ScrumPilot.API.Services;

public interface IPlanningPokerConnectionEvictor
{
    Task EvictUserFromProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default);
    Task EvictUserFromOrganizationAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default);
    Task EvictProjectAsync(int projectId, CancellationToken cancellationToken = default);
    Task EvictOrganizationAsync(
        int organizationId,
        CancellationToken cancellationToken = default);
}

public sealed class PlanningPokerConnectionEvictor(
    PlanningPokerSessionService sessions,
    IHubContext<PlanningPokerHub> hubContext) : IPlanningPokerConnectionEvictor
{
    public Task EvictUserFromProjectAsync(
        string userId,
        int projectId,
        CancellationToken cancellationToken = default) =>
        EvictAsync(sessions.RemoveUserFromProject(userId, projectId), cancellationToken);

    public Task EvictUserFromOrganizationAsync(
        string userId,
        int organizationId,
        CancellationToken cancellationToken = default) =>
        EvictAsync(
            sessions.RemoveUserFromOrganization(userId, organizationId),
            cancellationToken);

    public Task EvictProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default) =>
        EvictAsync(sessions.RemoveProject(projectId), cancellationToken);

    public Task EvictOrganizationAsync(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        EvictAsync(sessions.RemoveOrganization(organizationId), cancellationToken);

    private async Task EvictAsync(
        IReadOnlyList<PlanningPokerConnection> connections,
        CancellationToken cancellationToken)
    {
        foreach (var connection in connections)
        {
            var groupName = PlanningPokerHub.GroupName(connection.SessionKey);
            try
            {
                await hubContext.Groups.RemoveFromGroupAsync(
                    connection.ConnectionId,
                    groupName,
                    cancellationToken);
            }
            catch (Exception)
            {
                // State removal is authoritative; SignalR cleanup is best effort.
            }

            try
            {
                await hubContext.Clients.Group(groupName).SendAsync(
                    "UserLeft",
                    connection.ConnectionId,
                    cancellationToken);
            }
            catch (Exception)
            {
                // State removal is authoritative; fanout is best effort.
            }
        }
    }
}
