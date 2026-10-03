using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Extensions;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class OrganizationRepositoryTests
{
    [Fact]
    public void AddDataServices_RegistersOrganizationRepository()
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
        Assert.IsType<OrganizationRepository>(
            provider.GetRequiredService<IOrganizationRepository>());
    }

    [Fact]
    public async Task RemoveMemberAsync_SequentialLastOwnerAttemptPreservesOwner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(connection).Options;
        await using var context = new ScrumPilotContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Users.AddRange(User("one"), User("two"));
        var organization = new Organization
        {
            Name = "Owners",
            NormalizedName = "OWNERS",
            CreatedAt = DateTime.UtcNow
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        context.OrganizationMemberships.AddRange(
            Membership(organization.OrganizationId, "one", OrganizationRole.Owner),
            Membership(organization.OrganizationId, "two", OrganizationRole.Owner));
        await context.SaveChangesAsync();
        var repository = new OrganizationRepository(context);

        Assert.Equal(
            OrganizationMutationResult.Success,
            await repository.RemoveMemberAsync(organization.OrganizationId, "one", default));
        Assert.Equal(
            OrganizationMutationResult.LastOwner,
            await repository.RemoveMemberAsync(organization.OrganizationId, "two", default));
        Assert.True(await context.OrganizationMemberships.AnyAsync(x =>
            x.OrganizationId == organization.OrganizationId
            && x.UserId == "two"
            && x.Role == OrganizationRole.Owner));
    }

    [Fact]
    public async Task CreateAsync_DuplicateNormalizedName_TranslatesNameConflict()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(connection).Options;
        await using var context = new ScrumPilotContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Users.Add(User("owner"));
        context.Organizations.Add(new Organization
        {
            Name = "Existing",
            NormalizedName = "EXISTING",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var repository = new OrganizationRepository(context);

        await Assert.ThrowsAsync<OrganizationRepositoryConflictException>(() =>
            repository.CreateAsync(
                "Existing",
                "EXISTING",
                "owner",
                DateTime.UtcNow));
    }

    [Fact]
    public async Task CreateAsync_UnrelatedForeignKeyFailure_PropagatesDbUpdateException()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(connection).Options;
        await using var context = new ScrumPilotContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new OrganizationRepository(context);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.CreateAsync(
                "New organization",
                "NEW ORGANIZATION",
                "missing-owner",
                DateTime.UtcNow));

        Assert.IsNotType<OrganizationRepositoryConflictException>(exception);
    }

    [Fact]
    public async Task UpdateMemberRoleAsync_UnrelatedUniqueFailure_PropagatesDbUpdateException()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScrumPilotContext>().UseSqlite(connection).Options;
        await using var context = new ScrumPilotContext(options);
        await context.Database.EnsureCreatedAsync();
        var owner = User("owner");
        var member = User("member");
        context.Users.AddRange(owner, member);
        var organization = new Organization
        {
            Name = "Role update",
            NormalizedName = "ROLE UPDATE",
            CreatedAt = DateTime.UtcNow
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        context.OrganizationMemberships.AddRange(
            Membership(organization.OrganizationId, owner.Id, OrganizationRole.Owner),
            Membership(organization.OrganizationId, member.Id, OrganizationRole.Member));
        await context.SaveChangesAsync();
        member.NormalizedUserName = owner.NormalizedUserName;
        var repository = new OrganizationRepository(context);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.UpdateMemberRoleAsync(
                organization.OrganizationId,
                member.Id,
                OrganizationRole.Owner));
    }

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString()
    };

    private static OrganizationMembership Membership(
        int organizationId,
        string userId,
        OrganizationRole role) => new()
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            JoinedAt = DateTime.UtcNow
        };
}
