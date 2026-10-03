using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Extensions;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class ProjectAccessRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ScrumPilotContext _context;
    private readonly ProjectAccessRepository _repository;

    public ProjectAccessRepositoryTests()
    {
        _connection.Open();
        _context = new ScrumPilotContext(
            new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
        _repository = new ProjectAccessRepository(_context);
    }

    [Fact]
    public void AddDataServices_RegistersProjectAccessRepository()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:"
            })
            .Build();

        services.AddDataServices(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ProjectAccessRepository>(
            provider.GetRequiredService<IProjectAccessRepository>());
    }

    [Fact]
    public void ProjectService_ResolvesWithProductionDependencies()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:"
            })
            .Build();
        services.AddDataServices(configuration);
        services.AddScoped<IOrganizationAccessService, OrganizationAccessService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddSingleton(TimeProvider.System);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ProjectService>(scope.ServiceProvider.GetRequiredService<IProjectService>());
    }

    [Fact]
    public async Task GetAccessibleProjectsAsync_OwnerGetsAllAndMemberGetsOnlyExplicitProjects()
    {
        await SeedAsync();

        var ownerProjects = await _repository.GetAccessibleProjectsAsync("owner", 1);
        var memberProjects = await _repository.GetAccessibleProjectsAsync("member", 1);

        Assert.Equal([10, 11], ownerProjects.Select(x => x.ProjectId));
        Assert.Equal([10], memberProjects.Select(x => x.ProjectId));
    }

    [Fact]
    public async Task GetAccessibleProjectsAsync_ExcludesStaleMembershipAndDeletedOrganization()
    {
        await SeedAsync();

        Assert.Empty(await _repository.GetAccessibleProjectsAsync("stale", 1));
        Assert.Empty(await _repository.GetAccessibleProjectsAsync("deleted-owner", 2));
    }

    [Fact]
    public async Task GetMembersAsync_IncludesImplicitOwnersAndExplicitOrdinaryMembers()
    {
        await SeedAsync();

        var members = await _repository.GetMembersAsync(10);

        Assert.Collection(
            members.OrderBy(x => x.UserId),
            member =>
            {
                Assert.Equal("member", member.UserId);
                Assert.Equal(OrganizationRole.Member, member.OrganizationRole);
                Assert.True(member.HasExplicitAccess);
            },
            owner =>
            {
                Assert.Equal("owner", owner.UserId);
                Assert.Equal(OrganizationRole.Owner, owner.OrganizationRole);
                Assert.False(owner.HasExplicitAccess);
            });
    }

    [Fact]
    public async Task SetAccessAsync_RevokeClearsMatchingDefaultProject()
    {
        await SeedAsync();
        var member = await _context.Users.SingleAsync(x => x.Id == "member");
        member.DefaultProjectId = 10;
        await _context.SaveChangesAsync();

        await _repository.SetAccessAsync(10, "member", false, "owner", DateTime.UtcNow);

        Assert.False(await _context.ProjectMemberships.AnyAsync(x =>
            x.ProjectId == 10 && x.UserId == "member"));
        Assert.Null((await _context.Users.SingleAsync(x => x.Id == "member")).DefaultProjectId);
    }

    [Fact]
    public async Task DeleteAsync_ClearsAccessDefaultsPreferencesAndDeletesProject()
    {
        await SeedAsync();
        var owner = await _context.Users.SingleAsync(x => x.Id == "owner");
        owner.DefaultProjectId = 10;
        _context.UserDashboardPreferences.Add(new()
        {
            ProjectId = 10,
            UserId = "member",
            PreferencesJson = "{}"
        });
        await _context.SaveChangesAsync();

        Assert.True(await _repository.DeleteAsync(10));

        Assert.False(await _context.Projects.AnyAsync(x => x.ProjectId == 10));
        Assert.False(await _context.ProjectMemberships.AnyAsync(x => x.ProjectId == 10));
        Assert.False(await _context.UserDashboardPreferences.AnyAsync(x => x.ProjectId == 10));
        Assert.Null((await _context.Users.SingleAsync(x => x.Id == "owner")).DefaultProjectId);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task SeedAsync()
    {
        if (await _context.Users.AnyAsync())
        {
            return;
        }

        _context.Users.AddRange(
            User("owner"),
            User("member"),
            User("stale"),
            User("deleted-owner"));
        _context.Organizations.AddRange(
            Organization(1, "Active"),
            Organization(2, "Deleted", DateTime.UtcNow));
        _context.OrganizationMemberships.AddRange(
            Membership(1, "owner", OrganizationRole.Owner),
            Membership(1, "member", OrganizationRole.Member),
            Membership(2, "deleted-owner", OrganizationRole.Owner));
        _context.Projects.AddRange(
            Project(10, 1, "Alpha"),
            Project(11, 1, "Beta"),
            Project(20, 2, "Deleted"));
        _context.ProjectMemberships.AddRange(
            ProjectMembership(10, "member"),
            ProjectMembership(10, "stale"));
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

    private static Project Project(int id, int organizationId, string name) =>
        new() { ProjectId = id, OrganizationId = organizationId, ProjectName = name };

    private static ProjectMembership ProjectMembership(int projectId, string userId) =>
        new()
        {
            ProjectId = projectId,
            UserId = userId,
            GrantedAt = DateTime.UtcNow,
            GrantedByUserId = "owner"
        };
}
