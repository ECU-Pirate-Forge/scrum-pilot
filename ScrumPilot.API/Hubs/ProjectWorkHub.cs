using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ScrumPilot.API.Authorization;

namespace ScrumPilot.API.Hubs;

public sealed record ProjectWorkSubscription(string UserId, int ProjectId, int OrganizationId);

public sealed class ProjectWorkSubscriptions
{
    public ConcurrentDictionary<string, ProjectWorkSubscription> Connections { get; } = new();
}

[Authorize]
public sealed class ProjectWorkHub(
    IServiceScopeFactory scopes, ProjectWorkSubscriptions subscriptions) : Hub
{
    private const string OrganizationKey = "ScrumPilot.OrganizationId";

    public override Task OnConnectedAsync()
    {
        var segments = Context.GetHttpContext()?.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is not { Length: 4 } || segments[0] != "hubs" || segments[1] != "organizations"
            || segments[3] != "project-work" || !int.TryParse(segments[2], out var organizationId) || organizationId <= 0)
        {
            Context.Abort();
            throw new HubException("Project not found.");
        }
        Context.Items[OrganizationKey] = organizationId;
        return base.OnConnectedAsync();
    }

    public async Task Subscribe(int projectId)
    {
        subscriptions.Connections.TryRemove(Context.ConnectionId, out _);
        var user = Context.User?.Identity?.IsAuthenticated == true
            ? Context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        if (user is null || !Context.Items.TryGetValue(OrganizationKey, out var selected) || selected is not int organizationId)
            throw new HubException("Project not found.");
        var access = new InvocationOrganizationAccessService(scopes, organizationId, user);
        if (!await access.CanAccessProjectAsync(user, projectId, Context.ConnectionAborted)
            || await access.GetOrganizationIdForProjectAsync(projectId, Context.ConnectionAborted) != organizationId)
            throw new HubException("Project not found.");
        subscriptions.Connections[Context.ConnectionId] = new(user, projectId, organizationId);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Connections.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}
