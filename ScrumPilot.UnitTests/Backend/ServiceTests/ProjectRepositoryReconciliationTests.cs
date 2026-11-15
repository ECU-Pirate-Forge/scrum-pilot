using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class ProjectRepositoryReconciliationTests
{
    [Fact]
    public async Task RejectedReplacementReservationCannotRetireTheCurrentRepository()
    {
        await using var fixture = await PlanningSqlFixture.CreateAsync();
        var project = new Project { OrganizationId = fixture.Organization.OrganizationId, ProjectName = "Original" };
        fixture.Db.Projects.Add(project);
        await fixture.Db.SaveChangesAsync();
        var repository = new ProjectRepository
        {
            ProjectId = project.ProjectId,
            GitHubRepositoryId = 99,
            InstallationId = 42,
            Owner = "owner",
            Name = "repository",
            DefaultBranch = "main"
        };
        fixture.Db.ProjectRepositories.Add(repository);
        await fixture.RegisterAsync(repository);
        fixture.Control.GitHubRepositoryRegistrations.Add(new()
        {
            GitHubRepositoryId = 100,
            OrganizationId = fixture.Organization.OrganizationId,
            StorageKey = fixture.Organization.StorageKey,
            ProjectId = project.ProjectId + 1,
            Owner = "owner",
            Name = "repository",
            State = RepositoryRegistrationState.Pending
        });
        await fixture.Control.SaveChangesAsync();
        using var http = new HttpClient(new PlanningGitHubHandler { RepositoryId = 100 });
        using var rsa = RSA.Create(2048);
        var github = new GitHubAppClient(http, Options.Create(new GitHubAppOptions
        { AppId = "123", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }));
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns("owner");
        var controller = new ProjectRepositoryController(user, fixture.Access, fixture.Db, github,
            new(fixture.Db, github, fixture.Registry), fixture.Control, fixture.Registry, new(fixture.Control, github));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Configure(project.ProjectId,
            new(100, 42, "owner", "repository", "main", repository.Version), default));
        Assert.True(await fixture.Registry.IsCurrentAsync(repository, default));
    }

    [Theory]
    [InlineData(42)]
    [InlineData(43)]
    public async Task ReconciliationUsesVerifiedInstallationAndNeverRevivesAnOldPassingCheck(long installationId)
    {
        await using var fixture = await TaskPlanningFixture.CreateAsync();
        await fixture.ApproveAsync();
        var oldCheck = await new PullRequestScopeService(fixture.Db, fixture.GitHub, fixture.Specs, fixture.Control, fixture.Registry)
            .BindAsync(fixture.ProjectId, fixture.TaskId, 7, "owner", default);
        fixture.Http.UnavailableOriginalInstallation = installationId != 42;
        var before = fixture.Http.Requests.Count;
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns("owner");
        var access = Substitute.For<IOrganizationAccessService>();
        access.CanAccessProjectAsync("owner", fixture.ProjectId, Arg.Any<CancellationToken>()).Returns(true);
        access.GetOrganizationIdForProjectAsync(fixture.ProjectId, Arg.Any<CancellationToken>())
            .Returns(fixture.Selection.OrganizationId);
        access.IsOrganizationOwnerAsync("owner", fixture.Selection.OrganizationId!.Value, Arg.Any<CancellationToken>())
            .Returns(true);
        var controller = new ProjectRepositoryController(user, access, fixture.Db, fixture.GitHub,
            new(fixture.Db, fixture.GitHub, fixture.Registry), fixture.Control, fixture.Registry,
            new(fixture.Control, fixture.GitHub));
        var result = await controller.Configure(fixture.ProjectId,
            new(99, installationId, "owner", "repository", fixture.Repository.DefaultBranch, fixture.Repository.Version), default);
        Assert.IsType<OkObjectResult>(result);
        Assert.True(await fixture.Registry.IsCurrentAsync(fixture.Repository, default));
        Assert.False(await fixture.Registry.IsValidationCurrentAsync(fixture.Repository, oldCheck, default));
        Assert.True(fixture.Repository.Version > oldCheck.RepositoryVersion);
        Assert.Equal(installationId, (await fixture.Control.GitHubRepositoryRegistrations.SingleAsync()).InstallationId);
        if (installationId != 42)
            Assert.DoesNotContain("/app/installations/42/access_tokens", fixture.Http.Requests.Skip(before));
    }
}
