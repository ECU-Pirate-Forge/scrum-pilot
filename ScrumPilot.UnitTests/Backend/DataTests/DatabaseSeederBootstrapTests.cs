using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Extensions;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Seeders;
using ScrumPilot.Data.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class DatabaseSeederBootstrapTests
{
    private static readonly DateTime BootstrapTime = new(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SeedPirateForgeOrganizationAsync_EmptyDatabaseCreatesOrganization()
    {
        await using var database = await TestDatabase.CreateAsync();

        var organization = await DatabaseSeeder.SeedPirateForgeOrganizationAsync(
            database.Context,
            new FixedTimeProvider(BootstrapTime));

        Assert.Equal("Pirate Forge", organization.Name);
        Assert.Equal("PIRATE FORGE", organization.NormalizedName);
        Assert.Equal(BootstrapTime, organization.CreatedAt);
    }

    [Fact]
    public async Task SeedUsersAsync_CreatesAndReconcilesTylerAsConfirmedAdmin()
    {
        await using var database = await IdentityTestDatabase.CreateAsync();

        var created = await DatabaseSeeder.SeedUsersAsync(database.UserManager, database.RoleManager);
        var tyler = Assert.IsType<ApplicationUser>(
            await database.UserManager.FindByEmailAsync("Tyler@scrumpilot.xyz"));

        Assert.True(tyler.EmailConfirmed);
        Assert.True(await database.UserManager.IsInRoleAsync(tyler, "Admin"));
        Assert.Contains(tyler.Id, created);

        await database.UserManager.AddToRoleAsync(tyler, "Developer");
        await database.UserManager.RemoveFromRoleAsync(tyler, "Admin");
        tyler.EmailConfirmed = false;
        await database.UserManager.UpdateAsync(tyler);

        created = await DatabaseSeeder.SeedUsersAsync(database.UserManager, database.RoleManager);
        tyler = Assert.IsType<ApplicationUser>(
            await database.UserManager.FindByEmailAsync("Tyler@scrumpilot.xyz"));

        Assert.True(tyler.EmailConfirmed);
        Assert.True(await database.UserManager.IsInRoleAsync(tyler, "Admin"));
        Assert.True(await database.UserManager.IsInRoleAsync(tyler, "Developer"));
        Assert.Empty(created);
    }

    [Fact]
    public async Task SeedUsersAsync_RoleCreationFailureThrowsActionableError()
    {
        await using var database = await IdentityTestDatabase.CreateAsync(
            services => services.AddScoped<IRoleValidator<IdentityRole>, RejectingRoleValidator>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSeeder.SeedUsersAsync(database.UserManager, database.RoleManager));

        Assert.Contains("Unable to create seeded role Admin", exception.Message);
        Assert.Contains(RejectingRoleValidator.ErrorDescription, exception.Message);
        Assert.DoesNotContain("Password1234!", exception.Message);
    }

    [Fact]
    public async Task SeedUsersAsync_UserCreationFailureThrowsActionableError()
    {
        await using var database = await IdentityTestDatabase.CreateAsync(
            services => services.Configure<IdentityOptions>(
                options => options.Password.RequiredLength = 100));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSeeder.SeedUsersAsync(database.UserManager, database.RoleManager));

        Assert.Contains("Unable to create seeded user Tyler@scrumpilot.xyz", exception.Message);
        Assert.DoesNotContain("Password1234!", exception.Message);
    }

    [Fact]
    public async Task SeedProjectDataAsync_AssignsEveryNewProjectToPirateForge()
    {
        await using var database = await TestDatabase.CreateAsync();
        var organization = await DatabaseSeeder.SeedPirateForgeOrganizationAsync(
            database.Context,
            new FixedTimeProvider(BootstrapTime));

        await DatabaseSeeder.SeedProjectDataAsync(database.Context);

        var projects = await database.Context.Projects.AsNoTracking().ToListAsync();
        Assert.Equal(4, projects.Count);
        Assert.All(projects, project => Assert.Equal(organization.OrganizationId, project.OrganizationId));
    }

    [Fact]
    public async Task SeedProjectDataAsync_ScopesProjectNamesAndScrumPilotDataToPirateForge()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;
        var pirateForge = await AddOrganizationAsync(context);
        var otherOrganization = new Organization
        {
            Name = "Other",
            NormalizedName = "OTHER",
            CreatedAt = BootstrapTime
        };
        context.Organizations.Add(otherOrganization);
        await context.SaveChangesAsync();
        foreach (var name in new[] { "ScrumPilot", "Pulse", "FormFlow", "Sunflower Tracker" })
        {
            await AddProjectAsync(context, otherOrganization.OrganizationId, name);
        }

        await DatabaseSeeder.SeedProjectDataAsync(context);

        Assert.Equal(
            4,
            await context.Projects.CountAsync(x => x.OrganizationId == pirateForge.OrganizationId));
        Assert.Equal(
            4,
            await context.Projects.CountAsync(x => x.OrganizationId == otherOrganization.OrganizationId));
        Assert.Equal(
            5,
            await context.Sprints.CountAsync(
                x => context.Projects
                    .Where(project => project.OrganizationId == pirateForge.OrganizationId)
                    .Select(project => project.ProjectId)
                    .Contains(x.ProjectId)));
        Assert.False(
            await context.Sprints.AnyAsync(
                x => context.Projects
                    .Where(project => project.OrganizationId == otherOrganization.OrganizationId)
                    .Select(project => project.ProjectId)
                    .Contains(x.ProjectId)));
    }

    [Fact]
    public async Task SeedPirateForgeMembershipsAsync_ReconcilesRolesAndOnlyBackfillsNewMembers()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;
        var organization = await AddOrganizationAsync(context);
        var firstProject = await AddProjectAsync(context, organization.OrganizationId, "First");
        var secondProject = await AddProjectAsync(context, organization.OrganizationId, "Second");
        await AddRoleAsync(context, "admin-role", "Admin", "ADMIN");
        await AddUsersAsync(context, "admin-b", "owner-a", "existing-member", "new-member");
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = "admin-b", RoleId = "admin-role" });
        context.OrganizationMemberships.AddRange(
            Membership(organization.OrganizationId, "admin-b", OrganizationRole.Member),
            Membership(organization.OrganizationId, "owner-a", OrganizationRole.Owner),
            Membership(organization.OrganizationId, "existing-member", OrganizationRole.Member));
        context.ProjectMemberships.AddRange(
            Grant(firstProject.ProjectId, "admin-b", "owner-a"),
            Grant(firstProject.ProjectId, "owner-a", "owner-a"),
            Grant(firstProject.ProjectId, "existing-member", "owner-a"));
        await context.SaveChangesAsync();

        var timeProvider = new FixedTimeProvider(BootstrapTime);
        await DatabaseSeeder.SeedPirateForgeMembershipsAsync(
            context,
            timeProvider,
            ["new-member"]);

        Assert.Equal(OrganizationRole.Owner, await MembershipRoleAsync(context, organization.OrganizationId, "admin-b"));
        Assert.Equal(OrganizationRole.Owner, await MembershipRoleAsync(context, organization.OrganizationId, "owner-a"));
        Assert.Equal(OrganizationRole.Member, await MembershipRoleAsync(context, organization.OrganizationId, "existing-member"));
        Assert.Equal(OrganizationRole.Member, await MembershipRoleAsync(context, organization.OrganizationId, "new-member"));
        Assert.Empty(await ProjectIdsAsync(context, "admin-b"));
        Assert.Empty(await ProjectIdsAsync(context, "owner-a"));
        Assert.Equal([firstProject.ProjectId], await ProjectIdsAsync(context, "existing-member"));
        Assert.Equal([firstProject.ProjectId, secondProject.ProjectId], await ProjectIdsAsync(context, "new-member"));
        Assert.All(
            await context.ProjectMemberships.Where(x => x.UserId == "new-member").ToListAsync(),
            grant => Assert.Equal("admin-b", grant.GrantedByUserId));

        var membershipsBefore = await context.OrganizationMemberships.AsNoTracking()
            .OrderBy(x => x.UserId)
            .Select(x => new { x.UserId, x.Role, x.JoinedAt })
            .ToListAsync();
        var grantsBefore = await context.ProjectMemberships.AsNoTracking()
            .OrderBy(x => x.UserId).ThenBy(x => x.ProjectId)
            .Select(x => new { x.UserId, x.ProjectId, x.GrantedAt, x.GrantedByUserId })
            .ToListAsync();

        await DatabaseSeeder.SeedPirateForgeMembershipsAsync(context, timeProvider, []);

        Assert.Equal(membershipsBefore, await context.OrganizationMemberships.AsNoTracking()
            .OrderBy(x => x.UserId)
            .Select(x => new { x.UserId, x.Role, x.JoinedAt })
            .ToListAsync());
        Assert.Equal(grantsBefore, await context.ProjectMemberships.AsNoTracking()
            .OrderBy(x => x.UserId).ThenBy(x => x.ProjectId)
            .Select(x => new { x.UserId, x.ProjectId, x.GrantedAt, x.GrantedByUserId })
            .ToListAsync());

        var revoked = await context.ProjectMemberships.FindAsync(secondProject.ProjectId, "new-member");
        context.ProjectMemberships.Remove(Assert.IsType<ProjectMembership>(revoked));
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedPirateForgeMembershipsAsync(context, timeProvider, []);

        Assert.Equal([firstProject.ProjectId], await ProjectIdsAsync(context, "new-member"));
    }

    [Fact]
    public async Task SeedPirateForgeMembershipsAsync_RemovedMemberIsNotRecreated()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;
        var organization = await AddOrganizationAsync(context);
        var project = await AddProjectAsync(context, organization.OrganizationId, "First");
        await AddUsersAsync(context, "owner", "removed");
        context.OrganizationMemberships.AddRange(
            Membership(organization.OrganizationId, "owner", OrganizationRole.Owner),
            Membership(organization.OrganizationId, "removed", OrganizationRole.Member));
        context.ProjectMemberships.Add(Grant(project.ProjectId, "removed", "owner"));
        await context.SaveChangesAsync();

        context.ProjectMemberships.Remove(
            Assert.IsType<ProjectMembership>(
                await context.ProjectMemberships.FindAsync(project.ProjectId, "removed")));
        context.OrganizationMemberships.Remove(
            Assert.IsType<OrganizationMembership>(
                await context.OrganizationMemberships.FindAsync(organization.OrganizationId, "removed")));
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedPirateForgeMembershipsAsync(
            context,
            new FixedTimeProvider(BootstrapTime),
            []);

        Assert.False(await context.OrganizationMemberships.AnyAsync(x => x.UserId == "removed"));
        Assert.False(await context.ProjectMemberships.AnyAsync(x => x.UserId == "removed"));
    }

    [Fact]
    public async Task SeedPirateForgeMembershipsAsync_ExistingAdminWithoutMembershipIsNotAdded()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;
        var organization = await AddOrganizationAsync(context);
        await AddRoleAsync(context, "admin-role", "Admin", "ADMIN");
        await AddUsersAsync(context, "owner", "admin");
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = "admin", RoleId = "admin-role" });
        context.OrganizationMemberships.Add(
            Membership(organization.OrganizationId, "owner", OrganizationRole.Owner));
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedPirateForgeMembershipsAsync(
            context,
            new FixedTimeProvider(BootstrapTime),
            []);

        Assert.False(await context.OrganizationMemberships.AnyAsync(x => x.UserId == "admin"));
    }

    [Fact]
    public async Task SeedPirateForgeMembershipsAsync_ValidatorRejectsNoOwnerAndRetryCompletes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;
        var organization = await AddOrganizationAsync(context);
        var project = await AddProjectAsync(context, organization.OrganizationId, "ScrumPilot");
        await AddRoleAsync(context, "admin-role", "Admin", "ADMIN");
        await AddUsersAsync(context, "legacy-member", "new-admin", "new-member");
        context.OrganizationMemberships.Add(
            Membership(organization.OrganizationId, "legacy-member", OrganizationRole.Member));
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedPirateForgeMembershipsAsync(
            context,
            new FixedTimeProvider(BootstrapTime),
            ["new-admin", "new-member"]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new OrganizationBootstrapValidator(context).ValidateAsync());

        Assert.Equal(OrganizationBootstrapValidator.MissingOwnerMessage, exception.Message);
        Assert.Equal(
            ["legacy-member", "new-admin", "new-member"],
            await context.OrganizationMemberships.AsNoTracking()
                .OrderBy(x => x.UserId)
                .Select(x => x.UserId)
                .ToArrayAsync());
        Assert.Empty(await context.ProjectMemberships.AsNoTracking().ToListAsync());

        context.UserRoles.Add(
            new IdentityUserRole<string> { UserId = "new-admin", RoleId = "admin-role" });
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedPirateForgeMembershipsAsync(
            context,
            new FixedTimeProvider(BootstrapTime),
            ["new-admin", "new-member"]);

        Assert.Equal(
            OrganizationRole.Owner,
            await MembershipRoleAsync(context, organization.OrganizationId, "new-admin"));
        Assert.Equal(
            OrganizationRole.Member,
            await MembershipRoleAsync(context, organization.OrganizationId, "new-member"));
        Assert.Equal([project.ProjectId], await ProjectIdsAsync(context, "new-member"));
        Assert.Empty(await ProjectIdsAsync(context, "legacy-member"));
    }

    [Fact]
    public async Task SoftDeletedPirateForge_BootstrapAndValidationLeaveSeedDataUntouched()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;
        var organization = await AddOrganizationAsync(context);
        var project = await AddProjectAsync(context, organization.OrganizationId, "Existing");
        await AddUsersAsync(context, "historical-owner", "unassigned-user");
        context.OrganizationMemberships.Add(
            Membership(organization.OrganizationId, "historical-owner", OrganizationRole.Owner));
        organization.DeletedAt = BootstrapTime;
        await context.SaveChangesAsync();

        var returnedOrganization = await DatabaseSeeder.SeedPirateForgeOrganizationAsync(
            context,
            new FixedTimeProvider(BootstrapTime.AddDays(1)));
        await DatabaseSeeder.SeedProjectDataAsync(context);
        await DatabaseSeeder.SeedPirateForgeMembershipsAsync(
            context,
            new FixedTimeProvider(BootstrapTime.AddDays(1)),
            ["unassigned-user"]);
        await new OrganizationBootstrapValidator(context).ValidateAsync();

        Assert.Equal(BootstrapTime, returnedOrganization.DeletedAt);
        Assert.Equal(
            ["Existing"],
            await context.Projects.AsNoTracking()
                .Where(x => x.OrganizationId == organization.OrganizationId)
                .Select(x => x.ProjectName)
                .ToArrayAsync());
        Assert.Equal(
            ["historical-owner"],
            await context.OrganizationMemberships.AsNoTracking()
                .Where(x => x.OrganizationId == organization.OrganizationId)
                .Select(x => x.UserId)
                .ToArrayAsync());
        Assert.Empty(await context.ProjectMemberships.AsNoTracking().ToListAsync());
        Assert.Equal(project.ProjectId, await context.Projects.Select(x => x.ProjectId).SingleAsync());
    }

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    public void BootstrapRetryClassifier_RecognizesPostgresRaceErrors(string sqlState)
    {
        var exception = new DbUpdateException(
            "bootstrap failed",
            new PostgresException("race", "ERROR", "ERROR", sqlState));

        Assert.True(PirateForgeBootstrapExceptionClassifier.IsRetryable(exception));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(19, 1555)]
    [InlineData(19, 2067)]
    public void BootstrapRetryClassifier_RecognizesSqliteRaceErrors(
        int primaryErrorCode,
        int extendedErrorCode)
    {
        var exception = new DbUpdateException(
            "bootstrap failed",
            new SqliteException("race", primaryErrorCode, extendedErrorCode));

        Assert.True(PirateForgeBootstrapExceptionClassifier.IsRetryable(exception));
    }

    [Fact]
    public void BootstrapRetryClassifier_RejectsUnrelatedErrors()
    {
        Assert.False(
            PirateForgeBootstrapExceptionClassifier.IsRetryable(
                new SqliteException("foreign key failed", 19, 787)));
        Assert.False(
            PirateForgeBootstrapExceptionClassifier.IsRetryable(
                new InvalidOperationException("application failure")));
    }

    [Fact]
    public async Task RunAsync_ConcurrentContextsProduceOneCompleteBootstrap()
    {
        await using var database = await BootstrapTestDatabase.CreateAsync();
        var bootstrapper = database.Provider.GetRequiredService<PirateForgeBootstrapper>();
        await using var seedScope = database.Provider.CreateAsyncScope();
        var createdUserIds = await DatabaseSeeder.SeedUsersAsync(
            seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            seedScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());

        await Task.WhenAll(
            bootstrapper.RunAsync(createdUserIds),
            bootstrapper.RunAsync(createdUserIds));

        await using var scope = database.Provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ScrumPilotContext>();
        var organization = await context.Organizations.AsNoTracking().SingleAsync();
        Assert.Equal("PIRATE FORGE", organization.NormalizedName);
        Assert.Null(organization.DeletedAt);
        Assert.Equal(
            4,
            await context.Projects.CountAsync(
                x => x.OrganizationId == organization.OrganizationId));
        Assert.Equal(
            8,
            await context.OrganizationMemberships.CountAsync(
                x => x.OrganizationId == organization.OrganizationId));
        Assert.Equal(
            1,
            await context.OrganizationMemberships.CountAsync(
                x => x.OrganizationId == organization.OrganizationId
                     && x.Role == OrganizationRole.Owner));
        Assert.Equal(5, await context.Sprints.CountAsync());
    }

    [Fact]
    public async Task ValidateAsync_NoOwnerThrowsActionableMessage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var organization = await AddOrganizationAsync(database.Context);
        await AddUsersAsync(database.Context, "member");
        database.Context.OrganizationMemberships.Add(
            Membership(organization.OrganizationId, "member", OrganizationRole.Member));
        await database.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new OrganizationBootstrapValidator(database.Context).ValidateAsync());

        Assert.Equal(
            "Pirate Forge has no organization owner. Assign the global Admin role to an existing user before starting ScrumPilot.",
            exception.Message);
    }

    [Fact]
    public async Task ValidateAsync_ExistingOwnerSucceeds()
    {
        await using var database = await TestDatabase.CreateAsync();
        var organization = await AddOrganizationAsync(database.Context);
        await AddUsersAsync(database.Context, "owner");
        database.Context.OrganizationMemberships.Add(
            Membership(organization.OrganizationId, "owner", OrganizationRole.Owner));
        await database.Context.SaveChangesAsync();

        await new OrganizationBootstrapValidator(database.Context).ValidateAsync();
    }

    [Fact]
    public void AddDataServices_RegistersOrganizationBootstrapValidator()
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
        using var scope = provider.CreateScope();
        Assert.IsType<OrganizationBootstrapValidator>(
            scope.ServiceProvider.GetRequiredService<OrganizationBootstrapValidator>());
        Assert.IsType<PirateForgeBootstrapper>(
            scope.ServiceProvider.GetRequiredService<PirateForgeBootstrapper>());
    }

    private static async Task<Organization> AddOrganizationAsync(ScrumPilotContext context)
    {
        var organization = new Organization
        {
            Name = "Pirate Forge",
            NormalizedName = "PIRATE FORGE",
            CreatedAt = BootstrapTime
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        return organization;
    }

    private static async Task<Project> AddProjectAsync(
        ScrumPilotContext context,
        int organizationId,
        string name)
    {
        var project = new Project
        {
            OrganizationId = organizationId,
            ProjectName = name
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project;
    }

    private static async Task AddRoleAsync(
        ScrumPilotContext context,
        string id,
        string name,
        string normalizedName)
    {
        context.Roles.Add(new IdentityRole
        {
            Id = id,
            Name = name,
            NormalizedName = normalizedName
        });
        await context.SaveChangesAsync();
    }

    private static async Task AddUsersAsync(ScrumPilotContext context, params string[] ids)
    {
        context.Users.AddRange(ids.Select(id => new ApplicationUser
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant()
        }));
        await context.SaveChangesAsync();
    }

    private static OrganizationMembership Membership(
        int organizationId,
        string userId,
        OrganizationRole role) =>
        new()
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            JoinedAt = BootstrapTime.AddDays(-1)
        };

    private static ProjectMembership Grant(int projectId, string userId, string grantedByUserId) =>
        new()
        {
            ProjectId = projectId,
            UserId = userId,
            GrantedByUserId = grantedByUserId,
            GrantedAt = BootstrapTime.AddDays(-1)
        };

    private static async Task<OrganizationRole> MembershipRoleAsync(
        ScrumPilotContext context,
        int organizationId,
        string userId) =>
        (await context.OrganizationMemberships.FindAsync(organizationId, userId))!.Role;

    private static async Task<int[]> ProjectIdsAsync(ScrumPilotContext context, string userId) =>
        await context.ProjectMemberships.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.ProjectId)
            .Select(x => x.ProjectId)
            .ToArrayAsync();

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection, ScrumPilotContext context)
        {
            _connection = connection;
            Context = context;
        }

        public ScrumPilotContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var context = new ScrumPilotContext(
                new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class IdentityTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private IdentityTestDatabase(
            SqliteConnection connection,
            ServiceProvider provider,
            AsyncServiceScope scope,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _connection = connection;
            _provider = provider;
            _scope = scope;
            UserManager = userManager;
            RoleManager = roleManager;
        }

        public UserManager<ApplicationUser> UserManager { get; }
        public RoleManager<IdentityRole> RoleManager { get; }

        public static async Task<IdentityTestDatabase> CreateAsync(
            Action<IServiceCollection>? configureServices = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ScrumPilotContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ScrumPilotContext>();
            configureServices?.Invoke(services);
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ScrumPilotContext>().Database.EnsureCreatedAsync();
            return new(
                connection,
                provider,
                scope,
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RejectingRoleValidator : IRoleValidator<IdentityRole>
    {
        public const string ErrorDescription = "role creation rejected for test";

        public Task<IdentityResult> ValidateAsync(
            RoleManager<IdentityRole> manager,
            IdentityRole role) =>
            Task.FromResult(
                IdentityResult.Failed(new IdentityError { Description = ErrorDescription }));
    }

    private sealed class BootstrapTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _anchorConnection;

        private BootstrapTestDatabase(
            SqliteConnection anchorConnection,
            ServiceProvider provider)
        {
            _anchorConnection = anchorConnection;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<BootstrapTestDatabase> CreateAsync()
        {
            var databaseName = $"bootstrap-{Guid.NewGuid():N}";
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databaseName,
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 1
            }.ToString();
            var anchorConnection = new SqliteConnection(connectionString);
            await anchorConnection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ScrumPilotContext>(
                options => options.UseSqlite(connectionString));
            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ScrumPilotContext>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(BootstrapTime));
            services.AddSingleton<PirateForgeBootstrapper>();
            services.AddScoped<OrganizationBootstrapValidator>();
            var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<ScrumPilotContext>()
                .Database.EnsureCreatedAsync();
            return new(anchorConnection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await _anchorConnection.DisposeAsync();
        }
    }
}
