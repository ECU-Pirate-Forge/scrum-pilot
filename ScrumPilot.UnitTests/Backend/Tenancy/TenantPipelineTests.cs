using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using ScrumPilot.API.Services;
using NSubstitute;
using ScrumPilot.API.Services.AI;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

public sealed class TenantPipelineTests : IDisposable
{
    private readonly TenantStorageTests files = new();
    private WebApplicationFactory<Program>? application;
    private Action<IServiceCollection>? serviceOverrides;
    private HttpClient Client(string? user)
    {
        application ??= new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddConfiguration(files.Database.Configuration).AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-only-signing-key-with-at-least-32-characters",
                ["Jwt:Issuer"] = "tests",
                ["Jwt:Audience"] = "tests"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                serviceOverrides?.Invoke(services);
            });
        });
        var client = application.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (user != null) client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }

    [Fact]
    public async Task ProjectCreation_Executes201Result_WithOrganizationQualifiedLocation_AndOnePersistedProject()
    {
        var org = await files.Create("Create");
        using var owner = Client("owner");
        using var response = await owner.PostAsJsonAsync($"/api/organizations/{org.OrganizationId}/projects",
            new { projectName = "A work only", description = "Synthetic isolation check" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/organizations/{org.OrganizationId}/projects/1", response.Headers.Location.ToString());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(response.Headers.Location)).StatusCode);
        using var tenant = files.Storage.Open(org);
        Assert.Equal(1, await tenant.Projects.CountAsync());
    }

    [Fact]
    public async Task CommentCreation_ReturnsRetrievableOrganizationQualifiedLocation_WithCollidingIds()
    {
        var a = await files.Create("CommentsA");
        var b = await files.Create("CommentsB", "member");
        foreach (var org in new[] { a, b })
        {
            var projectId = await files.Project(org);
            using var tenant = files.Storage.Open(org);
            var pbi = new ProductBacklogItem { ProjectId = projectId, Title = org.Name };
            tenant.Stories.Add(pbi);
            await tenant.SaveChangesAsync();
            Assert.Equal(1, pbi.PbiId);
        }
        using var owner = Client("owner");
        using var member = Client("member");
        using var createdA = await owner.PostAsJsonAsync($"/api/organizations/{a.OrganizationId}/comments",
            new Comment { PbiId = 1, Body = "A only", UserId = "admin" });
        Assert.Equal(HttpStatusCode.Created, createdA.StatusCode);
        Assert.NotNull(createdA.Headers.Location);
        Assert.EndsWith($"/api/organizations/{a.OrganizationId}/comments/1", createdA.Headers.Location.ToString());
        using var createdB = await member.PostAsJsonAsync($"/api/organizations/{b.OrganizationId}/comments",
            new Comment { PbiId = 1, Body = "B only", UserId = "admin" });
        Assert.Equal(HttpStatusCode.Created, createdB.StatusCode);
        Assert.NotNull(createdB.Headers.Location);
        Assert.EndsWith($"/api/organizations/{b.OrganizationId}/comments/1", createdB.Headers.Location.ToString());

        var commentA = await owner.GetFromJsonAsync<Comment>(createdA.Headers.Location);
        var commentB = await member.GetFromJsonAsync<Comment>(createdB.Headers.Location);
        Assert.Equal("A only", commentA!.Body);
        Assert.Equal("owner", commentA.UserId);
        Assert.Equal("B only", commentB!.Body);
        Assert.Equal("member", commentB.UserId);
        Assert.Equal(commentA.CommentId, commentB.CommentId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(createdB.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(createdA.Headers.Location)).StatusCode);
        using var admin = Client("admin");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(createdB.Headers.Location)).StatusCode);

        var membership = new OrganizationMembership
        {
            OrganizationId = b.OrganizationId,
            UserId = "owner",
            Role = OrganizationRole.Member
        };
        files.Control.OrganizationMemberships.Add(membership);
        await files.Control.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(createdB.Headers.Location)).StatusCode);
        using (var tenant = files.Storage.Open(b))
        {
            Assert.Single(await tenant.Comments.ToListAsync());
            tenant.ProjectMemberships.Add(new()
            {
                ProjectId = 1,
                UserId = "owner",
                GrantedByUserId = "member",
                MembershipInstanceId = membership.MembershipInstanceId
            });
            await tenant.SaveChangesAsync();
        }
        Assert.Equal("B only", (await owner.GetFromJsonAsync<Comment>(createdB.Headers.Location))!.Body);
        using (var tenant = files.Storage.Open(a))
            Assert.Single(await tenant.Comments.ToListAsync());
    }

    [Fact]
    public async Task DelayedAiGeneration_DoesNotBlockAnotherProjectReadInSameOrganization()
    {
        var org = await files.Create("Concurrent");
        using (var tenant = files.Storage.Open(org))
        {
            tenant.Projects.Add(new() { OrganizationId = org.OrganizationId, ProjectName = "Concurrent" });
            await tenant.SaveChangesAsync();
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<List<ProductBacklogItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ai = Substitute.For<IStoryGenerationService>();
        ai.GenerateAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(_ => { started.TrySetResult(); return release.Task; });
        serviceOverrides = services => services.AddScoped<IStoryGenerationService>(_ => ai);
        using var owner = Client("owner");
        var generation = owner.PostAsJsonAsync($"/api/organizations/{org.OrganizationId}/pbi/generateAiPbis?projectId=1", new[] { "A delayed problem" });
        Task<HttpResponseMessage>? read = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            read = owner.GetAsync($"/api/organizations/{org.OrganizationId}/projects/1");
            Assert.Equal(HttpStatusCode.OK, (await read.WaitAsync(TimeSpan.FromSeconds(2))).StatusCode);
            // Generation must release its local and SQL work leases, not merely allow another reader.
            await Task.Run(() =>
            {
                using var lease = files.Storage.Acquire(org.OrganizationId);
                using var sqlLease = files.Storage.AcquireExclusiveSqlLease(org.StorageKey);
            }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult([new() { ProjectId = 1, Title = "Generated" }]);
            using var generated = await generation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
            if (read != null) (await read.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        }
    }

    [Fact]
    public async Task AiCompletion_RechecksCurrentMembershipBeforeReturningGeneratedWork()
    {
        var org = await files.Create("RevokedAI"); await files.Project(org);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<List<ProductBacklogItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ai = Substitute.For<IStoryGenerationService>();
        ai.GenerateAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.TrySetResult(); return release.Task; });
        serviceOverrides = services => services.AddScoped<IStoryGenerationService>(_ => ai);
        using var owner = Client("owner");
        var request = owner.PostAsJsonAsync($"/api/organizations/{org.OrganizationId}/pbi/generateAiPbis?projectId=1", new[] { "Synthetic task" });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await files.Control.OrganizationMemberships.Where(x => x.OrganizationId == org.OrganizationId && x.UserId == "owner").ExecuteDeleteAsync();
        }
        finally { release.TrySetResult([new() { Title = "Generated" }]); }
        using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var tenant = files.Storage.Open(org); Assert.Empty(await tenant.Stories.ToListAsync());
    }
    [Fact]
    public async Task Restore_PersistsMaintenanceBeforeAwaitingEviction_AndRejectsLatePokerJoin()
    {
        var org = await files.Create("RestoreRace");
        var membership = new OrganizationMembership { OrganizationId = org.OrganizationId, UserId = "member", Role = OrganizationRole.Member };
        files.Control.OrganizationMemberships.Add(membership); await files.Control.SaveChangesAsync();
        using (var tenant = files.Storage.Open(org))
        {
            tenant.Projects.Add(new() { OrganizationId = org.OrganizationId, ProjectName = "Race" });
            await tenant.SaveChangesAsync();
            tenant.ProjectMemberships.Add(new() { ProjectId = 1, UserId = "member", GrantedByUserId = "owner", MembershipInstanceId = membership.MembershipInstanceId });
            await tenant.SaveChangesAsync();
        }
        var backupId = await files.Operations.BackupAsync(org.OrganizationId);
        var evicting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var evictor = Substitute.For<IPlanningPokerConnectionEvictor>();
        evictor.EvictOrganizationAsync(org.OrganizationId, Arg.Any<CancellationToken>())
            .Returns(_ => { evicting.TrySetResult(); return release.Task; });
        serviceOverrides = services => services.AddSingleton(evictor);
        using var admin = Client("admin");
        await using var connection = Connection("member", org.OrganizationId);
        await connection.StartAsync();
        // Native staging validation, backup, and restore are SQL operations, not
        // subsecond coordination signals (measured 20.58s in the Release regression).
        var sqlOperationBudget = TimeSpan.FromMinutes(2);
        admin.Timeout = sqlOperationBudget;
        using var deadline = new CancellationTokenSource(sqlOperationBudget);
        var restoring = admin.PostAsync($"/api/organizations/{org.OrganizationId}/operations/restore/{backupId}",
            null, deadline.Token);
        try
        {
            await Task.WhenAny(evicting.Task, restoring).WaitAsync(sqlOperationBudget);
            Assert.False(restoring.IsCompleted, "Restore returned before reaching the controlled eviction.");
            await evicting.Task.WaitAsync(sqlOperationBudget);
            var state = await files.Control.Organizations.AsNoTracking().SingleAsync(x => x.OrganizationId == org.OrganizationId);
            Assert.Equal("Maintenance", state.StorageState);
            Assert.True(state.RestorePending);
            await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinSession", "Late member", 1));
            Assert.Null(application!.Services.GetRequiredService<PlanningPokerSessionService>().GetSessionKey(connection.ConnectionId!));
        }
        finally
        {
            release.TrySetResult();
            await restoring.WaitAsync(sqlOperationBudget);
        }
        using var restored = await restoring;
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinSession", "Still revoked", 1));
    }

    private HubConnection Connection(string user, int organizationId) => new HubConnectionBuilder().WithUrl(
        $"https://localhost/hubs/organizations/{organizationId}/planning-poker", options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.HttpMessageHandlerFactory = _ => application!.Server.CreateHandler();
            options.Headers["X-Test-User"] = user;
            options.WebSocketFactory = async (context, ct) =>
            {
                var client = application!.Server.CreateWebSocketClient();
                client.ConfigureRequest = request => request.Headers["X-Test-User"] = user;
                return await client.ConnectAsync(context.Uri, ct);
            };
        }).Build();

    [Fact]
    public async Task Pipeline_UsesRouteIdentityAndLocalIds_AdminNeedsMembership_AndNoDefaultFallback()
    {
        var a = await files.Create("A"); var b = await files.Create("B", "member");
        foreach (var org in new[] { a, b })
        {
            using var tenant = files.Storage.Open(org);
            tenant.Projects.Add(new() { ProjectName = org.Name, OrganizationId = org.OrganizationId });
            await tenant.SaveChangesAsync();
            tenant.Stories.Add(new() { ProjectId = 1, Title = "Pbi-" + org.Name }); await tenant.SaveChangesAsync();
        }
        files.Control.OrganizationMemberships.Add(new() { OrganizationId = b.OrganizationId, UserId = "owner", Role = OrganizationRole.Member });
        await files.Control.SaveChangesAsync();
        using var owner = Client("owner");
        var pathA = $"/api/organizations/{a.OrganizationId}/pbi/getAllPbis?projectId=1";
        var pathB = $"/api/organizations/{b.OrganizationId}/pbi/getAllPbis?projectId=1";
        var result = await owner.GetAsync(pathA);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("Pbi-A", await result.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(pathB)).StatusCode);
        using (var tenant = files.Storage.Open(b))
        {
            var membership = await files.Control.OrganizationMemberships.SingleAsync(x => x.OrganizationId == b.OrganizationId && x.UserId == "owner");
            tenant.ProjectMemberships.Add(new() { ProjectId = 1, UserId = "owner", MembershipInstanceId = membership.MembershipInstanceId, GrantedByUserId = "member" });
            await tenant.SaveChangesAsync();
        }
        result = await owner.GetAsync(pathB);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("Pbi-B", await result.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/pbi/getAllPbis?projectId=1")).StatusCode);
        using var admin = Client("admin");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(pathA)).StatusCode);
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(pathA)).StatusCode);
    }

    [Fact]
    public async Task Pipeline_MissingCatalogIsSafe503ForMembers_But404ForOthers_AndNeverCreates()
    {
        var org = await files.Create("A");
        using var sql = new SqlServerAdministrationContext(files.Storage);
        await sql.DropAsync(TenantStorage.Catalog(org.StorageKey), default);
        var endpoint = $"/api/organizations/{org.OrganizationId}/projects";
        using var owner = Client("owner");
        var response = await owner.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(files.Storage.Options.ControlConnectionString, await response.Content.ReadAsStringAsync());
        Assert.False(await sql.ExistsAsync(TenantStorage.Catalog(org.StorageKey)));
        using var admin = Client("admin");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(endpoint)).StatusCode);
        Assert.False(await sql.ExistsAsync(TenantStorage.Catalog(org.StorageKey)));
    }

    [Fact]
    public async Task Pipeline_MismatchedFileAndConflictingBodyFailClosed()
    {
        var org = await files.Create("A");
        using var owner = Client("owner");
        var conflicting = new StringContent("{\"organizationId\":999,\"projectName\":\"bad\"}", System.Text.Encoding.UTF8, "application/json");
        var response = await owner.PostAsync($"/api/organizations/{org.OrganizationId}/projects", conflicting);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var tenant = files.Storage.OpenFile(org.StorageKey))
        {
            (await tenant.TenantMetadata.SingleAsync()).StorageKey = Guid.NewGuid().ToString("N"); await tenant.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.GetAsync($"/api/organizations/{org.OrganizationId}/projects")).StatusCode);
    }

    [Fact]
    public async Task Pipeline_OperationsAreAdminOnly_AndExposeSafeProvisioningStatus()
    {
        var org = await files.Create("Operations");
        using var owner = Client("owner");
        using var admin = Client("admin");
        var path = $"/api/organizations/{org.OrganizationId}/operations";
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(path)).StatusCode);
        var directory = await admin.GetAsync("/api/organizations");
        Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        Assert.Contains("Operations", await directory.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/organizations/{org.OrganizationId}/projects")).StatusCode);
        var response = await admin.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"state\":\"Active\"", body);
        Assert.DoesNotContain(org.StorageKey, body);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(path + "/retry", null)).StatusCode);
    }

    [Fact]
    public async Task Pipeline_ReadinessRevalidatesCentralCatalog_AndDoesNotRecreateIt()
    {
        using var anonymous = Client(null);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
        using var sql = new SqlServerAdministrationContext(files.Storage);
        await sql.DropAsync(sql.ControlCatalog, default);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await anonymous.GetAsync("/health")).StatusCode);
        Assert.False(await sql.ExistsAsync(sql.ControlCatalog));
    }

    [Fact]
    public async Task SignalR_UsesConnectionOrganizationAndFreshMembershipScopesPerInvocation()
    {
        var org = await files.Create("Poker", "member");
        var membership = new OrganizationMembership { OrganizationId = org.OrganizationId, UserId = "owner", Role = OrganizationRole.Member };
        files.Control.OrganizationMemberships.Add(membership); await files.Control.SaveChangesAsync();
        using (var tenant = files.Storage.Open(org))
        {
            tenant.Projects.Add(new() { OrganizationId = org.OrganizationId, ProjectName = "Poker" });
            await tenant.SaveChangesAsync();
            tenant.ProjectMemberships.Add(new() { ProjectId = 1, UserId = "owner", MembershipInstanceId = membership.MembershipInstanceId, GrantedByUserId = "member" });
            await tenant.SaveChangesAsync();
        }
        using var owner = Client("owner");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/organizations/{org.OrganizationId}/projects/1")).StatusCode);
        await using var connection = new HubConnectionBuilder().WithUrl(
            $"https://localhost/hubs/organizations/{org.OrganizationId}/planning-poker", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => application!.Server.CreateHandler();
                options.Headers["X-Test-User"] = "owner";
                options.WebSocketFactory = async (context, ct) =>
                {
                    var client = application!.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request => request.Headers["X-Test-User"] = "owner";
                    return await client.ConnectAsync(context.Uri, ct);
                };
            }).Build();
        await connection.StartAsync();
        await connection.InvokeAsync("JoinSession", "untrusted-name", 1);
        await connection.InvokeAsync("SelectCard", 3);
        // A maintenance backup after invocation must not wait on a connection-held tenant context.
        await using (var scope = application!.Services.CreateAsyncScope())
        {
            var backup = await Task.Run(() => scope.ServiceProvider.GetRequiredService<TenantOperations>()
                .BackupAsync(org.OrganizationId)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Guid.TryParseExact(backup, "N", out _));
        }
        Assert.Equal(OrganizationMutationResult.Success,
            await new ScrumPilot.Data.Repositories.OrganizationRepository(files.Control).RemoveMemberAsync(org.OrganizationId, "owner"));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SelectCard", 5));
    }

    public void Dispose() { application?.Dispose(); files.Dispose(); }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].FirstOrDefault();
            if (user == null) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user), new(ClaimTypes.Name, user) };
            if (user == "admin") claims.Add(new(ClaimTypes.Role, "Admin"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }

    }

}
