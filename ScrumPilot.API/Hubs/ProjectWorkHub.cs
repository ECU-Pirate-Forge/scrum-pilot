using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ScrumPilot.API.Authorization;

namespace ScrumPilot.API.Hubs;

public sealed record ProjectWorkSubscription(string UserId, int ProjectId, int OrganizationId = 0);

public sealed class ProjectWorkSubscriptions
{
    public ConcurrentDictionary<string, ProjectWorkSubscription> Connections { get; } = new();
}

[Authorize]
public sealed class ProjectWorkHub(
    IOrganizationAccessService access, ProjectWorkSubscriptions subscriptions) : Hub
{
    public async Task Subscribe(int projectId)
    {
        subscriptions.Connections.TryRemove(Context.ConnectionId, out _);
        var user = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user is null || !await access.CanAccessProjectAsync(user, projectId, Context.ConnectionAborted))
            throw new HubException("Project not found.");
        var organizationId = await access.GetOrganizationIdForProjectAsync(projectId, Context.ConnectionAborted)
            ?? throw new HubException("Project not found.");
        subscriptions.Connections[Context.ConnectionId] = new(user, projectId, organizationId);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Connections.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}
