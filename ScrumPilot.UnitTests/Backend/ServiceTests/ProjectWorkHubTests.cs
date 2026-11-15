using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Tenancy;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class ProjectWorkHubTests
{
    [Fact]
    public async Task NotificationsReachBothAuthorizedClients_AndEvictRevokedMembers()
    {
        var subscriptions = new ProjectWorkSubscriptions();
        subscriptions.Connections["one"] = new("first", 7, 3);
        subscriptions.Connections["two"] = new("second", 7, 3);
        subscriptions.Connections["other-organization"] = new("third", 7, 4);
        var hub = Substitute.For<IHubContext<ProjectWorkHub>>();
        var first = Substitute.For<ISingleClientProxy>();
        var second = Substitute.For<ISingleClientProxy>();
        hub.Clients.Client("one").Returns(first);
        hub.Clients.Client("two").Returns(second);
        var access = Substitute.For<IOrganizationAccessService>();
        access.CanAccessProjectAsync(Arg.Any<string>(), 7, Arg.Any<CancellationToken>()).Returns(true);
        var notifier = new ProjectWorkNotifier(hub, subscriptions, access, NullLogger<ProjectWorkNotifier>.Instance,
            new TenantSelection { OrganizationId = 3 });
        await notifier.NotifyAsync(7, 4, default);
        await first.Received(1).SendCoreAsync("ProjectWorkChanged",
            Arg.Is<object?[]>(args => Equals(args[0], 7) && Equals(args[1], 4L)), Arg.Any<CancellationToken>());
        await second.Received(1).SendCoreAsync("ProjectWorkChanged", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
        second.ClearReceivedCalls();
        access.CanAccessProjectAsync("second", 7, Arg.Any<CancellationToken>()).Returns(false);
        await notifier.NotifyAsync(7, 5, default);
        await second.DidNotReceive().SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
        Assert.False(subscriptions.Connections.ContainsKey("two"));
        Assert.True(subscriptions.Connections.ContainsKey("other-organization"));
        hub.Clients.DidNotReceive().Client("other-organization");
    }

    [Fact]
    public async Task OrganizationRevocationRemovesAllSubscriptions_EvenWhenANotificationFails()
    {
        var subscriptions = new ProjectWorkSubscriptions();
        subscriptions.Connections["revoked"] = new("member", 7, 3);
        subscriptions.Connections["disconnected"] = new("member", 7, 3);
        subscriptions.Connections["other"] = new("member", 8, 4);
        var hub = Substitute.For<IHubContext<ProjectWorkHub>>();
        var client = Substitute.For<ISingleClientProxy>();
        hub.Clients.Client("revoked").Returns(client);
        var disconnected = Substitute.For<ISingleClientProxy>();
        hub.Clients.Client("disconnected").Returns(disconnected);
        disconnected.SendCoreAsync("ProjectAccessRevoked", Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("The connection ended.")));
        await new ProjectWorkConnectionEvictor(hub, subscriptions, NullLogger<ProjectWorkConnectionEvictor>.Instance)
            .EvictAsync(organizationId: 3, userId: "member");
        Assert.False(subscriptions.Connections.ContainsKey("revoked"));
        Assert.False(subscriptions.Connections.ContainsKey("disconnected"));
        Assert.True(subscriptions.Connections.ContainsKey("other"));
        await client.Received(1).SendCoreAsync("ProjectAccessRevoked",
            Arg.Is<object?[]>(args => Equals(args[0], 7)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejoiningARevokedProjectCannotKeepAnEarlierSubscription()
    {
        var access = Substitute.For<IOrganizationAccessService>();
        var subscriptions = new ProjectWorkSubscriptions();
        subscriptions.Connections["connection"] = new("member", 7, 3);
        var caller = Substitute.For<HubCallerContext>();
        caller.ConnectionId.Returns("connection");
        caller.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "member")], "test")));
        caller.Items.Returns(new Dictionary<object, object?> { ["ScrumPilot.OrganizationId"] = 3 });
        using var services = new ServiceCollection().AddScoped<TenantSelection>()
            .AddSingleton(access).BuildServiceProvider();
        var hub = new ProjectWorkHub(services.GetRequiredService<IServiceScopeFactory>(), subscriptions) { Context = caller };
        await Assert.ThrowsAsync<HubException>(() => hub.Subscribe(7));
        Assert.Empty(subscriptions.Connections);
    }

    [Fact]
    public async Task SubscribeUsesFreshRouteBoundAuthorizationAndRemovesRevokedSubscription()
    {
        var access = Substitute.For<IOrganizationAccessService>();
        access.IsOrganizationMemberAsync("member", 3, Arg.Any<CancellationToken>()).Returns(true);
        access.CanAccessProjectAsync("member", 7, Arg.Any<CancellationToken>()).Returns(true);
        access.GetOrganizationIdForProjectAsync(7, Arg.Any<CancellationToken>()).Returns(3);
        var scopesCreated = 0;
        using var services = new ServiceCollection().AddScoped<TenantSelection>()
            .AddScoped<IOrganizationAccessService>(provider =>
            {
                Assert.Equal(3, provider.GetRequiredService<TenantSelection>().OrganizationId);
                scopesCreated++;
                return access;
            }).BuildServiceProvider();
        var caller = Substitute.For<HubCallerContext>();
        caller.ConnectionId.Returns("connection");
        caller.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "member")], "test")));
        caller.Items.Returns(new Dictionary<object, object?> { ["ScrumPilot.OrganizationId"] = 3 });
        var subscriptions = new ProjectWorkSubscriptions();
        var hub = new ProjectWorkHub(services.GetRequiredService<IServiceScopeFactory>(), subscriptions) { Context = caller };

        await hub.Subscribe(7);
        Assert.Equal(new ProjectWorkSubscription("member", 7, 3), subscriptions.Connections["connection"]);
        Assert.Equal(2, scopesCreated);
        access.IsOrganizationMemberAsync("member", 3, Arg.Any<CancellationToken>()).Returns(false);
        await Assert.ThrowsAsync<HubException>(() => hub.Subscribe(7));
        Assert.Empty(subscriptions.Connections);
        Assert.Equal(3, scopesCreated);
    }

    [Fact]
    public async Task ProjectEvictionRequiresOrganizationAndDoesNotEvictEqualIdInAnotherCatalog()
    {
        var subscriptions = new ProjectWorkSubscriptions();
        subscriptions.Connections["first"] = new("member", 7, 3);
        subscriptions.Connections["other"] = new("member", 7, 4);
        var hub = Substitute.For<IHubContext<ProjectWorkHub>>();
        hub.Clients.Client("first").Returns(Substitute.For<ISingleClientProxy>());
        var evictor = new ProjectWorkConnectionEvictor(hub, subscriptions, NullLogger<ProjectWorkConnectionEvictor>.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => evictor.EvictAsync(projectId: 7));
        await evictor.EvictAsync(projectId: 7, organizationId: 3);
        Assert.False(subscriptions.Connections.ContainsKey("first"));
        Assert.True(subscriptions.Connections.ContainsKey("other"));
    }
}
