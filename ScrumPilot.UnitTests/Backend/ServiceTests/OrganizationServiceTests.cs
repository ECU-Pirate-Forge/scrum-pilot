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

public sealed class OrganizationServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ScrumPilotContext _context;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IOrganizationAccessService _access = Substitute.For<IOrganizationAccessService>();
    private readonly IPlanningPokerConnectionEvictor _evictor =
        Substitute.For<IPlanningPokerConnectionEvictor>();
    private readonly TestTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 3, 0, 0, TimeSpan.Zero));
    private readonly OrganizationService _service;

    public OrganizationServiceTests()
    {
        _connection.Open();
        _context = new ScrumPilotContext(
            new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _currentUser.UserId.Returns("creator");
        _service = new OrganizationService(
            new OrganizationRepository(_context),
            _currentUser,
            _access,
            _clock,
            _evictor);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyCurrentUsersActiveOrganizationsWithRole()
    {
        await SeedUserAsync("creator");
        await SeedUserAsync("other");
        var active = await SeedOrganizationAsync("Active", ("creator", OrganizationRole.Member));
        await SeedOrganizationAsync("Other", ("other", OrganizationRole.Owner));
        var deleted = await SeedOrganizationAsync("Deleted", ("creator", OrganizationRole.Owner));
        deleted.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync();

        var result = await _service.ListAsync();

        var organization = Assert.Single(result);
        Assert.Equal(active.OrganizationId, organization.OrganizationId);
        Assert.Equal(OrganizationRole.Member, organization.Role);
        Assert.False(organization.IsDeleted);
    }

    [Fact]
    public async Task ListDeletedAsync_ReturnsHistoricalOwnersAndAllForGlobalAdmin()
    {
        await SeedUserAsync("creator");
        await SeedUserAsync("other");
        var owned = await SeedOrganizationAsync(
            "Owned deleted",
            ("creator", OrganizationRole.Owner));
        var notOwned = await SeedOrganizationAsync(
            "Other deleted",
            ("other", OrganizationRole.Owner));
        var memberOnly = await SeedOrganizationAsync(
            "Member deleted",
            ("creator", OrganizationRole.Member));
        owned.DeletedAt = notOwned.DeletedAt = memberOnly.DeletedAt =
            _clock.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync();

        var ownerResult = await _service.ListDeletedAsync();

        Assert.Equal([owned.OrganizationId], ownerResult.Select(x => x.OrganizationId));
        Assert.All(ownerResult, x => Assert.True(x.IsDeleted));

        _currentUser.IsInRole("Admin").Returns(true);
        var adminResult = await _service.ListDeletedAsync();

        Assert.Equal(3, adminResult.Count);
        Assert.All(adminResult, x => Assert.True(x.IsDeleted));
        Assert.Equal(
            OrganizationRole.Member,
            adminResult.Single(x => x.OrganizationId == notOwned.OrganizationId).Role);
    }

    [Fact]
    public async Task CreateAsync_AdminCreatorDiffersFromOwner_ReturnsTruthfulCreationResult()
    {
        await SeedUserAsync("creator");
        await SeedUserAsync("owner");
        _currentUser.IsInRole("Admin").Returns(true);

        var result = await _service.CreateAsync(new("  Pirate Forge  ", "owner"));

        Assert.Equal("Pirate Forge", result.Name);
        Assert.Equal("owner", result.InitialOwnerUserId);
        Assert.Null(result.GetType().GetProperty("Role"));
        Assert.Equal(
            "PIRATE FORGE",
            await _context.Organizations.Select(x => x.NormalizedName).SingleAsync());
        Assert.True(await _context.OrganizationMemberships.AnyAsync(x =>
            x.OrganizationId == result.OrganizationId
            && x.UserId == "owner"
            && x.Role == OrganizationRole.Owner));
        Assert.False(await _context.OrganizationMemberships.AnyAsync(x => x.UserId == "creator"));
    }

    [Fact]
    public async Task CreateAsync_NonAdminIsForbidden()
    {
        await Assert.ThrowsAsync<OrganizationForbiddenException>(
            () => _service.CreateAsync(new("Pirate Forge", "owner")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyNameIsMalformed(string name)
    {
        _currentUser.IsInRole("Admin").Returns(true);

        await Assert.ThrowsAsync<OrganizationValidationException>(
            () => _service.CreateAsync(new(name, "owner")));
    }

    [Fact]
    public async Task CreateAsync_DuplicateSoftDeletedNameIsConflict()
    {
        await SeedUserAsync("owner");
        var organization = await SeedOrganizationAsync("Pirate Forge", ("owner", OrganizationRole.Owner));
        organization.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync();
        _currentUser.IsInRole("Admin").Returns(true);

        var exception = await Assert.ThrowsAsync<OrganizationConflictException>(
            () => _service.CreateAsync(new(" pirate forge ", "owner")));

        Assert.Contains("deleted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_UnknownInitialOwnerIsMalformed()
    {
        _currentUser.IsInRole("Admin").Returns(true);

        await Assert.ThrowsAsync<OrganizationValidationException>(
            () => _service.CreateAsync(new("Pirate Forge", "missing")));
    }

    [Fact]
    public async Task GetAsync_InaccessibleOrganizationIsNotFound()
    {
        _access.IsOrganizationMemberAsync("creator", 42, Arg.Any<CancellationToken>())
            .Returns(false);

        await Assert.ThrowsAsync<OrganizationNotFoundException>(() => _service.GetAsync(42));
    }

    [Fact]
    public async Task RenameAsync_OwnerCanRenameAndDuplicateIsConflict()
    {
        await SeedUserAsync("creator");
        var organization = await SeedOrganizationAsync("Original", ("creator", OrganizationRole.Owner));
        await SeedOrganizationAsync("Taken", ("creator", OrganizationRole.Member));
        _access.IsOrganizationOwnerAsync("creator", organization.OrganizationId, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<OrganizationConflictException>(
            () => _service.RenameAsync(organization.OrganizationId, new(" taken ")));
    }

    [Fact]
    public async Task RenameAsync_MemberWhoIsNotOwnerIsForbidden()
    {
        _access.IsOrganizationOwnerAsync("creator", 17, Arg.Any<CancellationToken>())
            .Returns(false);
        _access.IsOrganizationMemberAsync("creator", 17, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<OrganizationForbiddenException>(
            () => _service.RenameAsync(17, new("Renamed")));
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_CannotDemoteLastOwner()
    {
        await SeedUserAsync("creator");
        var organization = await SeedOrganizationAsync("Only Owner", ("creator", OrganizationRole.Owner));
        _access.IsOrganizationOwnerAsync("creator", organization.OrganizationId, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<OrganizationConflictException>(() =>
            _service.UpdateMemberRoleAsync(
                organization.OrganizationId,
                "creator",
                new(OrganizationRole.Member)));
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_DemotingOwnerClearsInaccessibleProjectDefault()
    {
        await SeedUserAsync("creator");
        await SeedUserAsync("second-owner");
        var organization = await SeedOrganizationAsync(
            "Owners",
            ("creator", OrganizationRole.Owner),
            ("second-owner", OrganizationRole.Owner));
        var project = new Project
        {
            ProjectName = "Implicit access",
            OrganizationId = organization.OrganizationId
        };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();
        var creator = await _context.Users.SingleAsync(user => user.Id == "creator");
        creator.DefaultOrganizationId = organization.OrganizationId;
        creator.DefaultProjectId = project.ProjectId;
        await _context.SaveChangesAsync();
        _access.IsOrganizationOwnerAsync(
                "creator",
                organization.OrganizationId,
                Arg.Any<CancellationToken>())
            .Returns(true);

        await _service.UpdateMemberRoleAsync(
            organization.OrganizationId,
            "creator",
            new(OrganizationRole.Member));

        await _evictor.Received(1).EvictUserFromOrganizationAsync(
            "creator",
            organization.OrganizationId,
            Arg.Any<CancellationToken>());
        await _context.Entry(creator).ReloadAsync();
        Assert.Equal(organization.OrganizationId, creator.DefaultOrganizationId);
        Assert.Null(creator.DefaultProjectId);
    }

    [Fact]
    public async Task RemoveMemberAsync_CleansProjectMembershipAndDefaults()
    {
        await SeedUserAsync("creator");
        await SeedUserAsync("member");
        var organization = await SeedOrganizationAsync(
            "Cleanup",
            ("creator", OrganizationRole.Owner),
            ("member", OrganizationRole.Member));
        var project = new Project { ProjectName = "Project", OrganizationId = organization.OrganizationId };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();
        _context.ProjectMemberships.Add(new()
        {
            ProjectId = project.ProjectId,
            UserId = "member",
            GrantedByUserId = "creator",
            GrantedAt = _clock.GetUtcNow().UtcDateTime
        });
        var member = await _context.Users.SingleAsync(x => x.Id == "member");
        member.DefaultOrganizationId = organization.OrganizationId;
        member.DefaultProjectId = project.ProjectId;
        await _context.SaveChangesAsync();
        _access.IsOrganizationOwnerAsync("creator", organization.OrganizationId, Arg.Any<CancellationToken>())
            .Returns(true);

        await _service.RemoveMemberAsync(organization.OrganizationId, "member");

        await _evictor.Received(1).EvictUserFromOrganizationAsync(
            "member",
            organization.OrganizationId,
            Arg.Any<CancellationToken>());
        Assert.False(await _context.OrganizationMemberships.AnyAsync(x => x.UserId == "member"));
        Assert.False(await _context.ProjectMemberships.AnyAsync(x => x.UserId == "member"));
        await _context.Entry(member).ReloadAsync();
        Assert.Null(member.DefaultOrganizationId);
        Assert.Null(member.DefaultProjectId);
    }

    [Fact]
    public async Task LeaveAsync_LastOwnerCannotLeave()
    {
        await SeedUserAsync("creator");
        var organization = await SeedOrganizationAsync("Only Owner", ("creator", OrganizationRole.Owner));
        _access.IsOrganizationMemberAsync("creator", organization.OrganizationId, Arg.Any<CancellationToken>())
            .Returns(true);

        await Assert.ThrowsAsync<OrganizationConflictException>(
            () => _service.LeaveAsync(organization.OrganizationId));
    }

    [Fact]
    public async Task LeaveAsync_MemberIsEvictedAfterRemoval()
    {
        await SeedUserAsync("creator");
        await SeedUserAsync("owner");
        var organization = await SeedOrganizationAsync(
            "Leave",
            ("owner", OrganizationRole.Owner),
            ("creator", OrganizationRole.Member));
        _access.IsOrganizationMemberAsync(
                "creator",
                organization.OrganizationId,
                Arg.Any<CancellationToken>())
            .Returns(true);

        await _service.LeaveAsync(organization.OrganizationId);

        await _evictor.Received(1).EvictUserFromOrganizationAsync(
            "creator",
            organization.OrganizationId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_EvictsAllOrganizationConnections()
    {
        await SeedUserAsync("creator");
        var organization = await SeedOrganizationAsync(
            "Delete",
            ("creator", OrganizationRole.Owner));
        _access.IsOrganizationOwnerAsync(
                "creator",
                organization.OrganizationId,
                Arg.Any<CancellationToken>())
            .Returns(true);

        await _service.DeleteAsync(organization.OrganizationId);

        await _evictor.Received(1).EvictOrganizationAsync(
            organization.OrganizationId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_HistoricalOwnerCanRestoreWithinThirtyDays()
    {
        await SeedUserAsync("creator");
        var organization = await SeedOrganizationAsync("Deleted", ("creator", OrganizationRole.Owner));
        organization.DeletedAt = _clock.GetUtcNow().AddDays(-30).UtcDateTime;
        await _context.SaveChangesAsync();

        var result = await _service.RestoreAsync(organization.OrganizationId);

        Assert.False(result.IsDeleted);
        Assert.Null((await _context.Organizations.FindAsync(organization.OrganizationId))!.DeletedAt);
    }

    [Fact]
    public async Task RestoreAsync_AfterThirtyDaysIsConflict()
    {
        await SeedUserAsync("creator");
        var organization = await SeedOrganizationAsync("Deleted", ("creator", OrganizationRole.Owner));
        organization.DeletedAt = _clock.GetUtcNow().AddDays(-30).AddTicks(-1).UtcDateTime;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<OrganizationConflictException>(
            () => _service.RestoreAsync(organization.OrganizationId));
    }

    [Fact]
    public async Task PurgeAsync_RequiresAdminAndThirtyDayOldDeletion()
    {
        await SeedUserAsync("creator");
        var organization = await SeedOrganizationAsync("Deleted", ("creator", OrganizationRole.Owner));
        var project = new Project
        {
            ProjectName = "Dependent project",
            OrganizationId = organization.OrganizationId
        };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();
        var creator = await _context.Users.SingleAsync(x => x.Id == "creator");
        creator.DefaultOrganizationId = organization.OrganizationId;
        creator.DefaultProjectId = project.ProjectId;
        organization.DeletedAt = _clock.GetUtcNow().AddDays(-30).UtcDateTime;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<OrganizationForbiddenException>(
            () => _service.PurgeAsync(organization.OrganizationId));

        _currentUser.IsInRole("Admin").Returns(true);
        await _service.PurgeAsync(organization.OrganizationId);
        await _evictor.Received(1).EvictOrganizationAsync(
            organization.OrganizationId,
            Arg.Any<CancellationToken>());
        Assert.Null(await _context.Organizations.FindAsync(organization.OrganizationId));
        Assert.False(await _context.Projects.AnyAsync(x => x.ProjectId == project.ProjectId));
        await _context.Entry(creator).ReloadAsync();
        Assert.Null(creator.DefaultOrganizationId);
        Assert.Null(creator.DefaultProjectId);
    }

    private async Task SeedUserAsync(string id)
    {
        _context.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            Email = $"{id}@example.com",
            NormalizedEmail = $"{id}@example.com".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        });
        await _context.SaveChangesAsync();
    }

    private async Task<Organization> SeedOrganizationAsync(
        string name,
        params (string UserId, OrganizationRole Role)[] members)
    {
        var organization = new Organization
        {
            Name = name,
            NormalizedName = name.Trim().ToUpperInvariant(),
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync();
        _context.OrganizationMemberships.AddRange(members.Select(x => new OrganizationMembership
        {
            OrganizationId = organization.OrganizationId,
            UserId = x.UserId,
            Role = x.Role,
            JoinedAt = _clock.GetUtcNow().UtcDateTime
        }));
        await _context.SaveChangesAsync();
        return organization;
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
