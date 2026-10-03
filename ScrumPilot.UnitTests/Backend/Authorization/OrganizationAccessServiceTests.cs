using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.Authorization;

public sealed class OrganizationAccessServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly QueryCounter _queryCounter = new();
    private ScrumPilotContext _context = null!;
    private OrganizationAccessService _service = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScrumPilotContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_queryCounter)
            .Options;
        _context = new ScrumPilotContext(options);
        await _context.Database.EnsureCreatedAsync();
        await SeedAsync();
        _service = new OrganizationAccessService(_context);
        _queryCounter.Reset();
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task OrganizationChecks_RecognizeMembersAndOwners()
    {
        Assert.True(await _service.IsOrganizationMemberAsync("owner", 1));
        Assert.True(await _service.IsOrganizationMemberAsync("member", 1));
        Assert.True(await _service.IsOrganizationOwnerAsync("owner", 1));
        Assert.False(await _service.IsOrganizationOwnerAsync("member", 1));
        Assert.False(await _service.IsOrganizationMemberAsync("outsider", 1));
    }

    [Fact]
    public async Task CanAccessProject_AllowsOwnerImplicitlyInOneQuery()
    {
        Assert.True(await _service.CanAccessProjectAsync("owner", 10));
        Assert.Equal(1, _queryCounter.Count);
    }

    [Fact]
    public async Task CanAccessProject_AllowsExplicitMemberOnlyForGrantedProject()
    {
        Assert.True(await _service.CanAccessProjectAsync("member", 10));
        Assert.False(await _service.CanAccessProjectAsync("member", 11));
    }

    [Fact]
    public async Task CanAccessProject_DeniesForeignOrganizationAndMissingProject()
    {
        Assert.False(await _service.CanAccessProjectAsync("foreign-owner", 10));
        Assert.False(await _service.CanAccessProjectAsync("owner", 999));
    }

    [Fact]
    public async Task DeletedOrganization_DeniesAllAccess()
    {
        Assert.False(await _service.IsOrganizationMemberAsync("deleted-owner", 3));
        Assert.False(await _service.IsOrganizationOwnerAsync("deleted-owner", 3));
        Assert.False(await _service.CanAccessProjectAsync("deleted-owner", 30));
        Assert.Null(await _service.GetOrganizationIdForProjectAsync(30));
        Assert.Null(await _service.GetOrganizationIdForPbiAsync(300));
        Assert.Null(await _service.GetOrganizationIdForSprintAsync(301));
        Assert.Null(await _service.GetOrganizationIdForEpicAsync(302));
    }

    [Fact]
    public async Task ResourceOrganizationLookups_ResolvePersistedHierarchy()
    {
        Assert.Equal(1, await _service.GetOrganizationIdForProjectAsync(10));
        Assert.Equal(1, await _service.GetOrganizationIdForPbiAsync(100));
        Assert.Equal(1, await _service.GetOrganizationIdForSprintAsync(101));
        Assert.Equal(1, await _service.GetOrganizationIdForEpicAsync(102));
    }

    [Fact]
    public async Task ResourceOrganizationLookups_ReturnNullForMissingIds()
    {
        Assert.Null(await _service.GetOrganizationIdForProjectAsync(999));
        Assert.Null(await _service.GetOrganizationIdForPbiAsync(999));
        Assert.Null(await _service.GetOrganizationIdForSprintAsync(999));
        Assert.Null(await _service.GetOrganizationIdForEpicAsync(999));
    }

    [Fact]
    public async Task HierarchyChecks_RequireMatchingPersistedProject()
    {
        Assert.True(await _service.SprintBelongsToProjectAsync(101, 10));
        Assert.True(await _service.EpicBelongsToProjectAsync(102, 10));
        Assert.True(await _service.PbiBelongsToProjectAsync(100, 10));
        Assert.False(await _service.SprintBelongsToProjectAsync(101, 20));
        Assert.False(await _service.EpicBelongsToProjectAsync(102, 20));
        Assert.False(await _service.PbiBelongsToProjectAsync(100, 20));
    }

    [Fact]
    public async Task UserCanBeAssignedToProject_UsesProjectAccessNotGlobalRole()
    {
        Assert.True(await _service.UserCanBeAssignedToProjectAsync("owner", 10));
        Assert.True(await _service.UserCanBeAssignedToProjectAsync("member", 10));
        Assert.False(await _service.UserCanBeAssignedToProjectAsync("admin", 10));
    }

    [Fact]
    public async Task CancellationToken_IsPropagatedToEfQueries()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.IsOrganizationMemberAsync("owner", 1, cancellation.Token));
    }

    private async Task SeedAsync()
    {
        _context.Users.AddRange(
            User("owner"),
            User("member"),
            User("outsider"),
            User("foreign-owner"),
            User("deleted-owner"),
            User("admin"));
        _context.Roles.Add(
            new IdentityRole
            {
                Id = "admin-role",
                Name = "Admin",
                NormalizedName = "ADMIN"
            });
        _context.UserRoles.Add(
            new IdentityUserRole<string> { UserId = "admin", RoleId = "admin-role" });
        _context.Organizations.AddRange(
            Organization(1, "Active"),
            Organization(2, "Foreign"),
            Organization(3, "Deleted", DateTime.UtcNow));
        _context.OrganizationMemberships.AddRange(
            Membership(1, "owner", OrganizationRole.Owner),
            Membership(1, "member", OrganizationRole.Member),
            Membership(2, "foreign-owner", OrganizationRole.Owner),
            Membership(3, "deleted-owner", OrganizationRole.Owner));
        _context.Projects.AddRange(
            Project(10, 1),
            Project(11, 1),
            Project(20, 2),
            Project(30, 3));
        _context.ProjectMemberships.Add(
            new ProjectMembership
            {
                ProjectId = 10,
                UserId = "member",
                GrantedByUserId = "owner",
                GrantedAt = DateTime.UtcNow
            });
        _context.Stories.AddRange(Pbi(100, 10), Pbi(300, 30));
        _context.Sprints.AddRange(Sprint(101, 10), Sprint(301, 30));
        _context.Epics.AddRange(Epic(102, 10), Epic(302, 30));
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

    private static Project Project(int id, int organizationId) =>
        new() { ProjectId = id, OrganizationId = organizationId, ProjectName = $"Project {id}" };

    private static ProductBacklogItem Pbi(int id, int projectId) =>
        new() { PbiId = id, ProjectId = projectId, Title = $"PBI {id}" };

    private static Sprint Sprint(int id, int projectId) =>
        new() { SprintId = id, ProjectId = projectId };

    private static Epic Epic(int id, int projectId) =>
        new() { EpicId = id, ProjectId = projectId, Name = $"Epic {id}" };

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public void Reset() => Count = 0;
    }
}
