using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class UserSettingsServiceTests
{
    [Theory]
    [InlineData(OrganizationRole.Member, true)]
    [InlineData(OrganizationRole.Owner, false)]
    public async Task UpdateSettingsAsync_AcceptsAccessibleOrganizationProjectPair(
        OrganizationRole role,
        bool explicitProjectAccess)
    {
        await using var fixture = await Fixture.CreateAsync(role, explicitProjectAccess);
        var normalizedUserName = fixture.User.NormalizedUserName;
        var normalizedEmail = fixture.User.NormalizedEmail;
        var securityStamp = fixture.User.SecurityStamp;
        var concurrencyStamp = fixture.User.ConcurrencyStamp;

        var result = await fixture.Service.UpdateSettingsAsync("user", new UserSettingsDto
        {
            Email = fixture.User.Email,
            DefaultOrganizationId = fixture.OrganizationId,
            DefaultProjectId = fixture.ProjectId
        });

        Assert.True(result.Succeeded);
        Assert.Equal(fixture.OrganizationId, fixture.User.DefaultOrganizationId);
        Assert.Equal(fixture.ProjectId, fixture.User.DefaultProjectId);
        var persisted = await fixture.ReadPersistedUserAsync();
        Assert.Equal(normalizedUserName, persisted.NormalizedUserName);
        Assert.Equal(normalizedEmail, persisted.NormalizedEmail);
        Assert.Equal(securityStamp, persisted.SecurityStamp);
        Assert.Equal(concurrencyStamp, persisted.ConcurrencyStamp);
        await fixture.UserManager.DidNotReceive()
            .UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task UpdateSettingsAsync_RejectsOrganizationWithoutMembership()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.UpdateSettingsAsync("user", new UserSettingsDto
        {
            Email = fixture.User.Email,
            DefaultOrganizationId = fixture.OtherOrganizationId
        });

        Assert.False(result.Succeeded);
        Assert.Null(fixture.User.DefaultOrganizationId);
    }

    [Fact]
    public async Task UpdateSettingsAsync_RejectsProjectWithoutExplicitAccessForMember()
    {
        await using var fixture = await Fixture.CreateAsync(OrganizationRole.Member);

        var result = await fixture.Service.UpdateSettingsAsync("user", new UserSettingsDto
        {
            Email = fixture.User.Email,
            DefaultOrganizationId = fixture.OrganizationId,
            DefaultProjectId = fixture.ProjectId
        });

        Assert.False(result.Succeeded);
        Assert.Null(fixture.User.DefaultProjectId);
    }

    [Fact]
    public async Task UpdateSettingsAsync_RejectsProjectFromAnotherOrganization()
    {
        await using var fixture = await Fixture.CreateAsync(
            OrganizationRole.Member,
            explicitProjectAccess: true,
            otherOrganizationMembership: true);

        var result = await fixture.Service.UpdateSettingsAsync("user", new UserSettingsDto
        {
            Email = fixture.User.Email,
            DefaultOrganizationId = fixture.OtherOrganizationId,
            DefaultProjectId = fixture.ProjectId
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateSettingsAsync_RejectsProjectWithoutOrganization()
    {
        await using var fixture = await Fixture.CreateAsync(
            OrganizationRole.Member,
            explicitProjectAccess: true);

        var result = await fixture.Service.UpdateSettingsAsync("user", new UserSettingsDto
        {
            Email = fixture.User.Email,
            DefaultProjectId = fixture.ProjectId
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateSettingsAsync_AllowsClearingDefaults()
    {
        await using var fixture = await Fixture.CreateAsync(
            OrganizationRole.Member,
            explicitProjectAccess: true);
        fixture.User.DefaultOrganizationId = fixture.OrganizationId;
        fixture.User.DefaultProjectId = fixture.ProjectId;

        var result = await fixture.Service.UpdateSettingsAsync("user", new UserSettingsDto
        {
            Email = fixture.User.Email
        });

        Assert.True(result.Succeeded);
        Assert.Null(fixture.User.DefaultOrganizationId);
        Assert.Null(fixture.User.DefaultProjectId);
    }

    [Fact]
    public async Task UpdateSettingsAsync_InvalidDefaultsDoNotPartiallyChangeProfile()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.User.DiscordUsername = "before";
        fixture.User.UiPreference = UiPreference.Light;

        var result = await fixture.Service.UpdateSettingsAsync("user", new UserSettingsDto
        {
            Email = fixture.User.Email,
            DiscordUsername = "after",
            UiPreference = UiPreference.Dark,
            DefaultOrganizationId = fixture.OtherOrganizationId
        });

        Assert.False(result.Succeeded);
        Assert.Equal("before", fixture.User.DiscordUsername);
        Assert.Equal(UiPreference.Light, fixture.User.UiPreference);
        await fixture.UserManager.DidNotReceive()
            .UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task UpdateSettingsAsync_RejectsEmailChangeWithoutMutatingProfile()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.User.DiscordUsername = "before";

        var result = await fixture.Service.UpdateSettingsAsync("user", new UserSettingsDto
        {
            Email = "changed@example.com",
            DiscordUsername = "after"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("original@example.com", fixture.User.Email);
        Assert.Equal("before", fixture.User.DiscordUsername);
        await fixture.UserManager.DidNotReceive()
            .UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task UpdateSettingsAsync_ConcurrentMembershipRevocationNeverSavesInaccessibleDefaults()
    {
        var databasePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            $"user-settings-concurrency-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Default Timeout=1";

        try
        {
            var baseOptions = new DbContextOptionsBuilder<ScrumPilotContext>()
                .UseSqlite(connectionString)
                .Options;
            int organizationId;
            int projectId;
            await using (var setup = new ScrumPilotContext(baseOptions))
            {
                await setup.Database.EnsureCreatedAsync();
                await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                setup.Users.Add(new ApplicationUser
                {
                    Id = "user",
                    UserName = "user",
                    NormalizedUserName = "USER",
                    Email = "original@example.com",
                    NormalizedEmail = "ORIGINAL@EXAMPLE.COM"
                });
                var organization = new Organization
                {
                    Name = "Organization",
                    NormalizedName = "ORGANIZATION",
                    CreatedAt = DateTime.UtcNow
                };
                setup.Organizations.Add(organization);
                await setup.SaveChangesAsync();
                var project = new Project
                {
                    OrganizationId = organization.OrganizationId,
                    ProjectName = "Project"
                };
                setup.Projects.Add(project);
                setup.OrganizationMemberships.Add(new OrganizationMembership
                {
                    OrganizationId = organization.OrganizationId,
                    UserId = "user",
                    Role = OrganizationRole.Owner,
                    JoinedAt = DateTime.UtcNow
                });
                await setup.SaveChangesAsync();
                organizationId = organization.OrganizationId;
                projectId = project.ProjectId;
            }

            var interceptor = new RevokeMembershipBeforeSaveInterceptor(
                baseOptions,
                organizationId,
                "user");
            var serviceOptions = new DbContextOptionsBuilder<ScrumPilotContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(interceptor)
                .Options;
            await using var context = new ScrumPilotContext(serviceOptions);
            var userManager = Substitute.For<UserManager<ApplicationUser>>(
                Substitute.For<IUserStore<ApplicationUser>>(),
                null, null, null, null, null, null, null, null);
            userManager.FindByIdAsync("user").Returns(_ =>
                context.Users.SingleAsync(user => user.Id == "user"));
            userManager.UpdateAsync(Arg.Any<ApplicationUser>())
                .Returns(async _ =>
                {
                    await context.SaveChangesAsync();
                    return IdentityResult.Success;
                });
            var service = new UserSettingsService(userManager, context);

            var result = await service.UpdateSettingsAsync("user", new UserSettingsDto
            {
                Email = "original@example.com",
                DefaultOrganizationId = organizationId,
                DefaultProjectId = projectId
            });

            Assert.False(result.Succeeded);
            Assert.True(interceptor.Attempted);
            await using var verification = new ScrumPilotContext(baseOptions);
            var saved = await verification.Users.AsNoTracking()
                .SingleAsync(user => user.Id == "user");
            var membershipExists = await verification.OrganizationMemberships.AnyAsync(
                membership => membership.UserId == "user"
                              && membership.OrganizationId == organizationId);
            Assert.True(
                membershipExists
                || (saved.DefaultOrganizationId is null && saved.DefaultProjectId is null));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }

    private sealed class RevokeMembershipBeforeSaveInterceptor(
        DbContextOptions<ScrumPilotContext> options,
        int organizationId,
        string userId) : SaveChangesInterceptor
    {
        private int _invoked;

        public bool Attempted { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _invoked, 1) != 0)
            {
                return result;
            }

            Attempted = true;
            await using var revocationContext = new ScrumPilotContext(options);
            await revocationContext.OrganizationMemberships
                .Where(membership =>
                    membership.OrganizationId == organizationId
                    && membership.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            return result;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ScrumPilotContext _context;

        private Fixture(
            SqliteConnection connection,
            ScrumPilotContext context,
            UserManager<ApplicationUser> userManager,
            ApplicationUser user,
            int organizationId,
            int otherOrganizationId,
            int projectId)
        {
            _connection = connection;
            _context = context;
            UserManager = userManager;
            User = user;
            OrganizationId = organizationId;
            OtherOrganizationId = otherOrganizationId;
            ProjectId = projectId;
            Service = new UserSettingsService(userManager, context);
        }

        public UserSettingsService Service { get; }
        public UserManager<ApplicationUser> UserManager { get; }
        public ApplicationUser User { get; }
        public int OrganizationId { get; }
        public int OtherOrganizationId { get; }
        public int ProjectId { get; }

        public Task<ApplicationUser> ReadPersistedUserAsync() =>
            _context.Users.AsNoTracking().SingleAsync(user => user.Id == User.Id);

        public static async Task<Fixture> CreateAsync(
            OrganizationRole? role = null,
            bool explicitProjectAccess = false,
            bool otherOrganizationMembership = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new ScrumPilotContext(
                new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();

            var user = new ApplicationUser
            {
                Id = "user",
                UserName = "user",
                NormalizedUserName = "USER",
                Email = "original@example.com",
                NormalizedEmail = "ORIGINAL@EXAMPLE.COM",
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            context.Users.Add(user);
            var organization = new Organization
            {
                Name = "Organization",
                NormalizedName = "ORGANIZATION",
                CreatedAt = DateTime.UtcNow
            };
            var otherOrganization = new Organization
            {
                Name = "Other",
                NormalizedName = "OTHER",
                CreatedAt = DateTime.UtcNow
            };
            context.Organizations.AddRange(organization, otherOrganization);
            await context.SaveChangesAsync();
            var project = new Project
            {
                OrganizationId = organization.OrganizationId,
                ProjectName = "Project"
            };
            context.Projects.Add(project);
            if (role.HasValue)
            {
                context.OrganizationMemberships.Add(new OrganizationMembership
                {
                    OrganizationId = organization.OrganizationId,
                    UserId = user.Id,
                    Role = role.Value,
                    JoinedAt = DateTime.UtcNow
                });
            }
            if (otherOrganizationMembership)
            {
                context.OrganizationMemberships.Add(new OrganizationMembership
                {
                    OrganizationId = otherOrganization.OrganizationId,
                    UserId = user.Id,
                    Role = OrganizationRole.Member,
                    JoinedAt = DateTime.UtcNow
                });
            }
            await context.SaveChangesAsync();
            if (explicitProjectAccess)
            {
                context.ProjectMemberships.Add(new ProjectMembership
                {
                    ProjectId = project.ProjectId,
                    UserId = user.Id,
                    GrantedAt = DateTime.UtcNow,
                    GrantedByUserId = user.Id
                });
                await context.SaveChangesAsync();
            }

            var userManager = Substitute.For<UserManager<ApplicationUser>>(
                Substitute.For<IUserStore<ApplicationUser>>(),
                null, null, null, null, null, null, null, null);
            userManager.FindByIdAsync(user.Id).Returns(user);
            userManager.UpdateAsync(user).Returns(IdentityResult.Success);

            return new Fixture(
                connection,
                context,
                userManager,
                user,
                organization.OrganizationId,
                otherOrganization.OrganizationId,
                project.ProjectId);
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
