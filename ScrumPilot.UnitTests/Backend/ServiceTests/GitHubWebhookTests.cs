using System.Security.Cryptography;
using System.Text;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class GitHubWebhookTests
{
    [Fact]
    public async Task IntegrationIsDisabledEvenWhenCredentialsRemainConfigured()
    {
        using var handler = new PlanningGitHubHandler();
        using var http = new HttpClient(handler);
        var github = new GitHubAppClient(http, Options.Create(new GitHubAppOptions
        { AppId = "123", PrivateKeyPem = "must-not-be-read", WebhookSecret = "test-secret" }), Options.Create(new FeatureOptions()));
        Assert.False(github.IsConfigured);
        Assert.False(github.IsWebhookConfigured);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            github.GetTreeAsync(42, 99, "owner", "repository", "main", default));
        Assert.Contains("disabled", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void SignatureValidatesExactBodyAndRejectsTampering()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"synchronize\"}");
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), body));
        var client = new GitHubAppClient(new HttpClient(), Options.Create(new GitHubAppOptions { WebhookSecret = "test-secret" }),
            Options.Create(new FeatureOptions { GitHubIntegration = true }));
        Assert.True(client.VerifyWebhook(body, signature));
        Assert.False(client.VerifyWebhook(Encoding.UTF8.GetBytes("{}"), signature));
        Assert.False(client.VerifyWebhook(body, "sha256=" + new string('z', 64)));
        Assert.False(client.VerifyWebhook(body, ""));
    }

    [Fact]
    public async Task ForgedDeliveryCannotQueryControlStorageOrSelectTenant()
    {
        await using var control = new ControlPlaneContext(new DbContextOptionsBuilder<ControlPlaneContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true")
            .AddInterceptors(new RejectDatabaseOpen()).Options);
        using var http = new HttpClient();
        var github = new GitHubAppClient(http, Options.Create(new GitHubAppOptions { WebhookSecret = "test-secret" }),
            Options.Create(new FeatureOptions { GitHubIntegration = true }));
        var scopes = Substitute.For<IServiceScopeFactory>();
        var controller = Controller(github, control, scopes);
        SetRequest(controller, "not json", "pull_request");
        controller.Request.Headers["X-Hub-Signature-256"] = "sha256=" + new string('0', 64);
        Assert.IsType<UnauthorizedResult>(await controller.Receive(default));
        scopes.DidNotReceive().CreateScope();
    }

    [Theory]
    [InlineData("{", "pull_request")]
    [InlineData("[]", "pull_request")]
    [InlineData("{\"repository\":[],\"installation\":{\"id\":42},\"number\":7}", "pull_request")]
    [InlineData("{\"repository\":{\"id\":\"99\"},\"installation\":{\"id\":42},\"number\":7}", "pull_request")]
    [InlineData("{\"action\":2,\"installation\":{\"id\":42}}", "installation")]
    [InlineData("{\"action\":\"removed\",\"installation\":{\"id\":42},\"repositories_removed\":[null]}", "installation_repositories")]
    public async Task SignedMalformedPayloadIsBadRequestWithoutTenantSelection(string body, string eventName)
    {
        await using var fixture = await TaskPlanningFixture.CreateAsync();
        using var http = new HttpClient();
        var github = new GitHubAppClient(http, Options.Create(new GitHubAppOptions { WebhookSecret = "test-secret" }),
            Options.Create(new FeatureOptions { GitHubIntegration = true }));
        var scopes = Substitute.For<IServiceScopeFactory>();
        var controller = Controller(github, fixture.Control, scopes);
        SetRequest(controller, body, eventName);
        Assert.IsType<BadRequestObjectResult>(await controller.Receive(default));
        scopes.DidNotReceive().CreateScope();
    }

    [Fact]
    public async Task SignedPullRequestSelectsRegisteredOrganizationBeforeResolvingWorkAndDeduplicates()
    {
        await using var fixture = await TaskPlanningFixture.CreateAsync();
        await fixture.ApproveAsync();
        var scopeService = new PullRequestScopeService(fixture.Db, fixture.GitHub, fixture.Specs, fixture.Control, fixture.Registry);
        await scopeService.BindAsync(fixture.ProjectId, fixture.TaskId, 7, "owner", default);
        var resolved = 0;
        using var services = new ServiceCollection().AddScoped<TenantSelection>().AddScoped(provider =>
        {
            Assert.Equal(fixture.Selection.OrganizationId, provider.GetRequiredService<TenantSelection>().OrganizationId);
            resolved++;
            return scopeService;
        }).BuildServiceProvider();
        using var http = new HttpClient();
        var github = new GitHubAppClient(http, Options.Create(new GitHubAppOptions { WebhookSecret = "test-secret" }),
            Options.Create(new FeatureOptions { GitHubIntegration = true }));
        var controller = Controller(github, fixture.Control, services.GetRequiredService<IServiceScopeFactory>());
        SetRequest(controller, "{\"repository\":{\"id\":99},\"installation\":{\"id\":42},\"number\":7}", "pull_request");
        Assert.IsType<NoContentResult>(await controller.Receive(default));
        Assert.Equal(1, resolved);
        controller.Request.Body.Position = 0;
        Assert.IsType<NoContentResult>(await controller.Receive(default));
        Assert.Equal(1, resolved);
        Assert.Single(await fixture.Control.GitHubWebhookDeliveries.ToListAsync());
    }

    [Fact]
    public async Task SignedRepositoryRemovalSuspendsOnlyRemovedAuthorityAndInvalidatesItsCentralHeads()
    {
        await using var fixture = await TaskPlanningFixture.CreateAsync();
        await fixture.ApproveAsync();
        await new PullRequestScopeService(fixture.Db, fixture.GitHub, fixture.Specs, fixture.Control, fixture.Registry)
            .BindAsync(fixture.ProjectId, fixture.TaskId, 7, "owner", default);
        var registration = await fixture.Control.GitHubRepositoryRegistrations.SingleAsync();
        var other = new GitHubRepositoryRegistration
        {
            GitHubRepositoryId = 100,
            OrganizationId = registration.OrganizationId,
            StorageKey = registration.StorageKey,
            ProjectId = fixture.ProjectId + 1,
            InstallationId = 42,
            Owner = "owner",
            Name = "other",
            State = RepositoryRegistrationState.Active
        };
        fixture.Control.GitHubRepositoryRegistrations.Add(other);
        await fixture.Control.SaveChangesAsync();
        fixture.Http.Checks.Clear();
        using var http = new HttpClient();
        var github = new GitHubAppClient(http, Options.Create(new GitHubAppOptions { WebhookSecret = "test-secret" }),
            Options.Create(new FeatureOptions { GitHubIntegration = true }));
        var scopes = Substitute.For<IServiceScopeFactory>();
        var controller = Controller(github, fixture.Control, scopes, new(fixture.Control, fixture.GitHub));
        SetRequest(controller,
            "{\"action\":\"removed\",\"installation\":{\"id\":42},\"repositories_removed\":[{\"id\":99}]}",
            "installation_repositories");
        Assert.IsType<NoContentResult>(await controller.Receive(default));
        Assert.Equal(RepositoryRegistrationState.Suspended, registration.State);
        Assert.Equal(RepositoryRegistrationState.Active, other.State);
        Assert.All(fixture.Http.Checks, item => Assert.Equal("failure", item.GetProperty("conclusion").GetString()));
        Assert.NotEmpty(fixture.Http.Checks);
        var count = fixture.Http.Checks.Count;
        controller.Request.Body.Position = 0;
        Assert.IsType<NoContentResult>(await controller.Receive(default));
        Assert.Equal(count, fixture.Http.Checks.Count);
        scopes.DidNotReceive().CreateScope();
    }

    private static GitHubWebhooksController Controller(GitHubAppClient github, ControlPlaneContext control,
        IServiceScopeFactory scopes, GitHubCheckRecovery? recovery = null)
    {
        var hub = Substitute.For<IHubContext<ProjectWorkHub>>();
        var lifecycle = new PlanningIntegrationLifecycle(control, recovery ?? new(control, github),
            new(hub, new ProjectWorkSubscriptions(), NullLogger<ProjectWorkConnectionEvictor>.Instance),
            Substitute.For<IPlanningPokerConnectionEvictor>(), NullLogger<PlanningIntegrationLifecycle>.Instance);
        return new(github, control, new(control, new TenantSelection(),
            Options.Create(new FeatureOptions { GitHubIntegration = true })), scopes, lifecycle)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
    }

    private static void SetRequest(GitHubWebhooksController controller, string body, string eventName)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        controller.Request.Body = new MemoryStream(bytes);
        controller.Request.Headers["X-GitHub-Event"] = eventName;
        controller.Request.Headers["X-GitHub-Delivery"] = Guid.NewGuid().ToString();
        controller.Request.Headers["X-Hub-Signature-256"] =
            "sha256=" + Convert.ToHexString(HMACSHA256.HashData("test-secret"u8, bytes));
    }

    private sealed class RejectDatabaseOpen : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("An unsigned delivery must not access the control database.");
    }
}
