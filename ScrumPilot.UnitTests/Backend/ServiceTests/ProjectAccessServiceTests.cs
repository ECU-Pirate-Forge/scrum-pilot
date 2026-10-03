using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class ProjectAccessServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ScrumPilotContext _context;
    private readonly IOrganizationAccessService _access = Substitute.For<IOrganizationAccessService>();
    private readonly IPlanningPokerConnectionEvictor _evictor =
        Substitute.For<IPlanningPokerConnectionEvictor>();
    private readonly ProjectService _service;

    public ProjectAccessServiceTests()
    {
        _connection.Open();
        _context = new ScrumPilotContext(
            new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _service = new ProjectService(
            new ProjectAccessRepository(_context),
            _access,
            new TestTimeProvider(new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.Zero)),
            _evictor);
    }

    [Fact]
    public async Task GetAccessibleProjectsAsync_OwnerHasImplicitAccessAndMemberNeedsExplicitAccess()
    {
        await SeedAsync();
        _access.IsOrganizationMemberAsync(Arg.Any<string>(), 1, Arg.Any<CancellationToken>())
            .Returns(true);

        var ownerProjects = await _service.GetAccessibleProjectsAsync("owner", 1);
        var memberProjects = await _service.GetAccessibleProjectsAsync("member", 1);

        Assert.Equal(2, ownerProjects.Count);
        Assert.Single(memberProjects);
        Assert.Equal(10, memberProjects[0].ProjectId);
    }

    [Theory]
    [InlineData("outsider", 1)]
    [InlineData("deleted-owner", 2)]
    public async Task GetAccessibleProjectsAsync_ForeignOrDeletedOrganizationIsNotFound(
        string userId,
        int organizationId)
    {
        _access.IsOrganizationMemberAsync(userId, organizationId, Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<ProjectNotFoundException>(
            () => _service.GetAccessibleProjectsAsync(userId, organizationId));
    }

    [Fact]
    public async Task GetAccessibleProjectAsync_NoAccessIsNotFound()
    {
        _access.CanAccessProjectAsync("member", 11, Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<ProjectNotFoundException>(
            () => _service.GetAccessibleProjectAsync("member", 11));
    }

    [Fact]
    public async Task GetAccessibleProjectAsync_OwnerHasImplicitAccess()
    {
        await SeedAsync();
        _access.CanAccessProjectAsync("owner", 11, Arg.Any<CancellationToken>())
            .Returns(true);

        var project = await _service.GetAccessibleProjectAsync("owner", 11);

        Assert.Equal(11, project.ProjectId);
    }

    [Fact]
    public async Task CreateAsync_MemberIsForbiddenAndOutsiderIsNotFound()
    {
        _access.IsOrganizationOwnerAsync("member", 1, Arg.Any<CancellationToken>())
            .Returns(false);
        _access.IsOrganizationMemberAsync("member", 1, Arg.Any<CancellationToken>())
            .Returns(true);
        _access.IsOrganizationOwnerAsync("outsider", 1, Arg.Any<CancellationToken>())
            .Returns(false);
        _access.IsOrganizationMemberAsync("outsider", 1, Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<ProjectForbiddenException>(
            () => _service.CreateAsync("member", 1, new("Project", null)));
        await Assert.ThrowsAsync<ProjectNotFoundException>(
            () => _service.CreateAsync("outsider", 1, new("Project", null)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_RejectsEmptyProjectName(string name)
    {
        _access.IsOrganizationOwnerAsync("owner", 1, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<ProjectValidationException>(
            () => _service.CreateAsync("owner", 1, new(name, null)));
    }

    [Fact]
    public async Task UpdateAsync_WhitelistsNameAndDescriptionAndKeepsOrganization()
    {
        await SeedAsync();
        _access.CanAccessProjectAsync("owner", 10, Arg.Any<CancellationToken>())
            .Returns(true);
        _access.GetOrganizationIdForProjectAsync(10, Arg.Any<CancellationToken>())
            .Returns(1);
        _access.IsOrganizationOwnerAsync("owner", 1, Arg.Any<CancellationToken>())
            .Returns(true);

        var updated = await _service.UpdateAsync("owner", 10, new(" Renamed ", "Updated"));

        Assert.Equal("Renamed", updated.ProjectName);
        Assert.Equal("Updated", updated.Description);
        Assert.Equal(1, updated.OrganizationId);
    }

    [Fact]
    public async Task UpdateAsync_AccessibleNonOwnerIsForbidden()
    {
        _access.CanAccessProjectAsync("member", 10, Arg.Any<CancellationToken>())
            .Returns(true);
        _access.GetOrganizationIdForProjectAsync(10, Arg.Any<CancellationToken>())
            .Returns(1);
        _access.IsOrganizationOwnerAsync("member", 1, Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<ProjectForbiddenException>(
            () => _service.UpdateAsync("member", 10, new("Changed", null)));
    }

    [Fact]
    public async Task DeleteAsync_EvictsAllProjectConnections()
    {
        await SeedAsync();
        ConfigureOwnerMutation();

        await _service.DeleteAsync("owner", 10);

        await _evictor.Received(1).EvictProjectAsync(
            10,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAccessAsync_RejectsForeignTargetAndOrganizationOwner()
    {
        await SeedAsync();
        ConfigureOwnerMutation();

        await Assert.ThrowsAsync<ProjectValidationException>(
            () => _service.SetAccessAsync("owner", 10, "outsider", new(true)));
        await Assert.ThrowsAsync<ProjectConflictException>(
            () => _service.SetAccessAsync("owner", 10, "owner", new(true)));
    }

    [Fact]
    public async Task SetAccessAsync_MapsRepositoryConcurrencyToConflict()
    {
        var repository = Substitute.For<IProjectAccessRepository>();
        repository.SetAccessAsync(
                10,
                "member",
                true,
                "owner",
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ProjectAccessMutationResult>>(
                _ => throw new ProjectAccessConcurrencyException(
                    "concurrent",
                    new InvalidOperationException()));
        var service = new ProjectService(
            repository,
            _access,
            new TestTimeProvider(new DateTimeOffset(2026, 10, 3, 4, 0, 0, TimeSpan.Zero)),
            _evictor);
        ConfigureOwnerMutation();

        await Assert.ThrowsAsync<ProjectConflictException>(
            () => service.SetAccessAsync("owner", 10, "member", new(true)));
    }

    [Fact]
    public async Task SetAccessAsync_GrantAndRevokeUpdatesExplicitAccessAndDefault()
    {
        await SeedAsync();
        ConfigureOwnerMutation();
        var member = await _context.Users.SingleAsync(x => x.Id == "member");
        member.DefaultProjectId = 10;
        await _context.SaveChangesAsync();

        await _service.SetAccessAsync("owner", 10, "member", new(false));
        await _evictor.Received(1).EvictUserFromProjectAsync(
            "member",
            10,
            Arg.Any<CancellationToken>());
        await _service.SetAccessAsync("owner", 10, "member", new(true));

        Assert.True(await _context.ProjectMemberships.AnyAsync(x =>
            x.ProjectId == 10 && x.UserId == "member"));
        Assert.Null((await _context.Users.SingleAsync(x => x.Id == "member")).DefaultProjectId);
    }

    [Fact]
    public async Task SetAccessAsync_RevokePreservesDifferentProjectDefault()
    {
        await SeedAsync();
        ConfigureOwnerMutation();
        var member = await _context.Users.SingleAsync(x => x.Id == "member");
        member.DefaultProjectId = 11;
        await _context.SaveChangesAsync();

        await _service.SetAccessAsync("owner", 10, "member", new(false));

        await _context.Entry(member).ReloadAsync();
        Assert.Equal(11, member.DefaultProjectId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesProjectAndClearsMatchingDefaults()
    {
        await SeedAsync();
        ConfigureOwnerMutation();
        var member = await _context.Users.SingleAsync(x => x.Id == "member");
        member.DefaultProjectId = 10;
        await _context.SaveChangesAsync();

        await _service.DeleteAsync("owner", 10);

        Assert.False(await _context.Projects.AnyAsync(x => x.ProjectId == 10));
        await _context.Entry(member).ReloadAsync();
        Assert.Null(member.DefaultProjectId);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private void ConfigureOwnerMutation()
    {
        _access.CanAccessProjectAsync("owner", 10, Arg.Any<CancellationToken>())
            .Returns(true);
        _access.GetOrganizationIdForProjectAsync(10, Arg.Any<CancellationToken>())
            .Returns(1);
        _access.IsOrganizationOwnerAsync("owner", 1, Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private async Task SeedAsync()
    {
        if (await _context.Users.AnyAsync())
        {
            return;
        }

        _context.Users.AddRange(User("owner"), User("member"), User("outsider"), User("deleted-owner"));
        _context.Organizations.AddRange(
            Organization(1, "Active"),
            Organization(2, "Deleted", DateTime.UtcNow));
        _context.OrganizationMemberships.AddRange(
            Membership(1, "owner", OrganizationRole.Owner),
            Membership(1, "member", OrganizationRole.Member),
            Membership(2, "deleted-owner", OrganizationRole.Owner));
        _context.Projects.AddRange(
            new Project { ProjectId = 10, OrganizationId = 1, ProjectName = "Alpha" },
            new Project { ProjectId = 11, OrganizationId = 1, ProjectName = "Beta" });
        _context.ProjectMemberships.Add(new()
        {
            ProjectId = 10,
            UserId = "member",
            GrantedByUserId = "owner",
            GrantedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private static ApplicationUser User(string id) =>
        new() { Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant() };

    private static Organization Organization(int id, string name, DateTime? deletedAt = null) =>
        new()
        {
            OrganizationId = id,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow,
            DeletedAt = deletedAt
        };

    private static OrganizationMembership Membership(
        int organizationId,
        string userId,
        OrganizationRole role) =>
        new()
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow
        };

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
