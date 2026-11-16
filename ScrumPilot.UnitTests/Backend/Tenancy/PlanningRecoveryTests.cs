using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Hubs;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.ServiceTests;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.Tenancy;

public sealed class PlanningRecoveryTests
{
    [Fact]
    public async Task PurgeCannotReportCompletionWhilePublishedHeadRetirementIsUnconfirmedAndCanRetry()
    {
        using var fixture = new SqlTestDatabase();
        var organization = new Organization { Name = "Retirement", NormalizedName = "RETIREMENT" };
        fixture.Control.Organizations.Add(organization);
        await fixture.Control.SaveChangesAsync();
        await fixture.Operations.ProvisionAsync(organization.OrganizationId);
        var registration = Registration(organization, 1, 99);
        fixture.Control.GitHubRepositoryRegistrations.Add(registration);
        await fixture.Control.SaveChangesAsync();
        fixture.Control.PullRequestScopeAttempts.Add(new()
        {
            RegistrationId = registration.RegistrationId,
            OrganizationId = organization.OrganizationId,
            StorageKey = organization.StorageKey,
            ProjectId = 1,
            ProjectRepositoryId = 1,
            PullRequestNumber = 7,
            HeadSha = TaskPlanningFixture.HeadSha
        });
        organization.DeletedAt = DateTime.UtcNow.AddDays(-31);
        await fixture.Control.SaveChangesAsync();
        var handler = new PlanningGitHubHandler
        {
            BeforeCheckResponse = (_, _) => throw new HttpRequestException("Synthetic Checks outage.")
        };
        using var client = new HttpClient(handler);
        using var rsa = RSA.Create(2048);
        var github = new GitHubAppClient(client, Options.Create(new GitHubAppOptions
        { AppId = "123", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }),
            Options.Create(new ScrumPilot.Shared.Models.FeatureOptions { GitHubIntegration = true }));
        var observer = new PlanningIntegrationLifecycle(fixture.Control, new(fixture.Control, github),
            new(Substitute.For<IHubContext<ProjectWorkHub>>(), new(), NullLogger<ProjectWorkConnectionEvictor>.Instance),
            Substitute.For<IPlanningPokerConnectionEvictor>(), NullLogger<PlanningIntegrationLifecycle>.Instance);
        var operations = new TenantOperations(fixture.Control, fixture.Storage, [observer]);
        await Assert.ThrowsAsync<TenantUnavailableException>(() => operations.PurgeAsync(organization.OrganizationId, DateTime.UtcNow));
        Assert.Equal("Purging", organization.StorageState);
        Assert.Equal(RepositoryRegistrationState.Retiring, registration.State);
        Assert.Contains("unconfirmed", registration.RecoveryError);
        using (var admin = new SqlServerAdministrationContext(fixture.Storage))
            Assert.True(await admin.ExistsAsync(TenantStorage.Catalog(organization.StorageKey)));
        handler.BeforeCheckResponse = null;
        Assert.True(await operations.PurgeAsync(organization.OrganizationId, DateTime.UtcNow));
        Assert.Equal("Purged", organization.StorageState);
        Assert.Equal(RepositoryRegistrationState.Retired, registration.State);
        Assert.Single(await fixture.Control.PullRequestScopeAttempts.ToListAsync());
    }

    [Fact]
    public async Task RestoringOlderTenantRetainsNewCentralHeadsSuspendsOnlyItsAuthorityAndLeavesOtherTenantUntouched()
    {
        using var fixture = new SqlTestDatabase();
        var first = new Organization { Name = "First", NormalizedName = "FIRST" };
        var second = new Organization { Name = "Second", NormalizedName = "SECOND" };
        fixture.Control.Organizations.AddRange(first, second);
        await fixture.Control.SaveChangesAsync();
        await fixture.Operations.ProvisionAsync(first.OrganizationId);
        await fixture.Operations.ProvisionAsync(second.OrganizationId);
        var local = fixture.Storage.Open(first);
        await using var other = fixture.Storage.Open(second);
        try
        {
            var project = new Project { OrganizationId = first.OrganizationId, ProjectName = "before backup" };
            var otherProject = new Project { OrganizationId = second.OrganizationId, ProjectName = "other original" };
            local.Projects.Add(project);
            other.Projects.Add(otherProject);
            await local.SaveChangesAsync();
            await other.SaveChangesAsync();
            Assert.Equal(project.ProjectId, otherProject.ProjectId);
            var registration = Registration(first, project.ProjectId, 99);
            var otherRegistration = Registration(second, otherProject.ProjectId, 100);
            fixture.Control.GitHubRepositoryRegistrations.AddRange(registration, otherRegistration);
            await fixture.Control.SaveChangesAsync();
            var backup = await fixture.Operations.BackupAsync(first.OrganizationId);
            project.ProjectName = "after backup";
            otherProject.ProjectName = "other committed change";
            await local.SaveChangesAsync();
            await other.SaveChangesAsync();
            fixture.Control.PullRequestScopeAttempts.Add(new()
            {
                RegistrationId = registration.RegistrationId,
                OrganizationId = first.OrganizationId,
                StorageKey = first.StorageKey,
                ProjectId = project.ProjectId,
                ProjectRepositoryId = 1,
                PullRequestNumber = 7,
                HeadSha = TaskPlanningFixture.HeadSha
            });
            await fixture.Control.SaveChangesAsync();
            await local.DisposeAsync();

            var handler = new PlanningGitHubHandler();
            using var client = new HttpClient(handler);
            using var rsa = RSA.Create(2048);
            var github = new GitHubAppClient(client, Options.Create(new GitHubAppOptions
            { AppId = "123", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }),
                Options.Create(new ScrumPilot.Shared.Models.FeatureOptions { GitHubIntegration = true }));
            var hub = Substitute.For<IHubContext<ProjectWorkHub>>();
            var subscriptions = new ProjectWorkSubscriptions();
            subscriptions.Connections["first"] = new("owner", project.ProjectId, first.OrganizationId);
            subscriptions.Connections["second"] = new("owner", otherProject.ProjectId, second.OrganizationId);
            hub.Clients.Client("first").Returns(Substitute.For<ISingleClientProxy>());
            var observer = new PlanningIntegrationLifecycle(fixture.Control, new(fixture.Control, github),
                new(hub, subscriptions, NullLogger<ProjectWorkConnectionEvictor>.Instance),
                Substitute.For<IPlanningPokerConnectionEvictor>(), NullLogger<PlanningIntegrationLifecycle>.Instance);
            var operations = new TenantOperations(fixture.Control, fixture.Storage, [observer]);

            await operations.RestoreAsync(first.OrganizationId, backup);

            await using var restored = fixture.Storage.Open(first);
            Assert.Equal("before backup", await restored.Projects.Select(item => item.ProjectName).SingleAsync());
            Assert.Equal("other committed change", await other.Projects.AsNoTracking().Select(item => item.ProjectName).SingleAsync());
            Assert.Single(await fixture.Control.PullRequestScopeAttempts.AsNoTracking().ToListAsync());
            Assert.Equal(RepositoryRegistrationState.Suspended, registration.State);
            Assert.Equal(RepositoryRegistrationState.Active, otherRegistration.State);
            Assert.False(subscriptions.Connections.ContainsKey("first"));
            Assert.True(subscriptions.Connections.ContainsKey("second"));
            var invalidation = Assert.Single(handler.Checks);
            Assert.Equal(TaskPlanningFixture.HeadSha, invalidation.GetProperty("head_sha").GetString());
            Assert.Equal("failure", invalidation.GetProperty("conclusion").GetString());
        }
        finally
        {
            await local.DisposeAsync();
        }
    }

    private static GitHubRepositoryRegistration Registration(Organization organization, int projectId, long repositoryId) => new()
    {
        GitHubRepositoryId = repositoryId,
        OrganizationId = organization.OrganizationId,
        StorageKey = organization.StorageKey,
        ProjectId = projectId,
        ProjectRepositoryId = 1,
        InstallationId = 42,
        Owner = "owner",
        Name = "repository",
        ConfigurationVersion = 1,
        State = RepositoryRegistrationState.Active
    };
}
