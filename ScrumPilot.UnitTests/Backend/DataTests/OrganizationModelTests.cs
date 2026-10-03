using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.DataTests;

public sealed class OrganizationModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ScrumPilotContext _context;
    private readonly IModel _model;

    public OrganizationModelTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ScrumPilotContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ScrumPilotContext(options);
        _context.Database.EnsureCreated();
        _model = _context.Model;
    }

    [Fact]
    public void Context_ExposesOrganizationDbSets()
    {
        AssertDbSet<Organization>("Organizations");
        AssertDbSet<OrganizationMembership>("OrganizationMemberships");
        AssertDbSet<ProjectMembership>("ProjectMemberships");
        AssertDbSet<OrganizationInvitation>("OrganizationInvitations");
    }

    [Fact]
    public void ApplicationUser_OrganizationPropertiesExistAndCollectionsAreInitialized()
    {
        var user = new ApplicationUser();

        Assert.NotNull(typeof(ApplicationUser).GetProperty("DefaultOrganizationId"));
        Assert.IsAssignableFrom<ICollection<OrganizationMembership>>(
            typeof(ApplicationUser).GetProperty("OrganizationMemberships")!.GetValue(user));
        Assert.IsAssignableFrom<ICollection<ProjectMembership>>(
            typeof(ApplicationUser).GetProperty("ProjectMemberships")!.GetValue(user));
    }

    [Fact]
    public void Memberships_HaveCompositeKeysInTenantThenUserOrder()
    {
        Assert.Equal(
            [nameof(OrganizationMembership.OrganizationId), nameof(OrganizationMembership.UserId)],
            Entity<OrganizationMembership>().FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(
            [nameof(ProjectMembership.ProjectId), nameof(ProjectMembership.UserId)],
            Entity<ProjectMembership>().FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Organization_HasRequiredUniqueNameAndConcurrencyToken()
    {
        var organization = Entity<Organization>();

        Assert.Equal("Organizations", organization.GetTableName());
        Assert.False(organization.FindProperty(nameof(Organization.Name))!.IsNullable);
        Assert.Equal(200, organization.FindProperty(nameof(Organization.Name))!.GetMaxLength());
        Assert.False(organization.FindProperty(nameof(Organization.NormalizedName))!.IsNullable);
        Assert.Equal(200, organization.FindProperty(nameof(Organization.NormalizedName))!.GetMaxLength());
        Assert.True(organization.FindProperty(nameof(Organization.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(
            organization.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .SequenceEqual([nameof(Organization.NormalizedName)]));
    }

    [Fact]
    public void SaveChanges_AssignsTokenToAddedOrganization()
    {
        var organization = CreateOrganization("New Organization");
        _context.Organizations.Add(organization);

        _context.SaveChanges();

        Assert.NotEmpty(organization.RowVersion);
    }

    [Fact]
    public async Task SaveChangesAsync_RotatesTokenWhenOrganizationIsUpdated()
    {
        var organization = CreateOrganization("Original Organization");
        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync();
        var originalToken = organization.RowVersion.ToArray();

        organization.Name = "Updated Organization";
        organization.NormalizedName = "UPDATED ORGANIZATION";
        await _context.SaveChangesAsync();

        Assert.NotEmpty(organization.RowVersion);
        Assert.NotEqual(originalToken, organization.RowVersion);
    }

    [Fact]
    public async Task SaveChangesAsync_ThrowsWhenOrganizationWasUpdatedByAnotherContext()
    {
        var databaseName = $"organization-concurrency-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var keepAliveConnection = new SqliteConnection(connectionString);
        await keepAliveConnection.OpenAsync();
        var options = new DbContextOptionsBuilder<ScrumPilotContext>()
            .UseSqlite(connectionString)
            .Options;

        await using (var setupContext = new ScrumPilotContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            setupContext.Organizations.Add(CreateOrganization("Shared Organization"));
            await setupContext.SaveChangesAsync();
        }

        await using var firstContext = new ScrumPilotContext(options);
        await using var secondContext = new ScrumPilotContext(options);
        var firstOrganization = await firstContext.Organizations.SingleAsync();
        var secondOrganization = await secondContext.Organizations.SingleAsync();

        firstOrganization.Name = "First Update";
        await firstContext.SaveChangesAsync();
        secondOrganization.Name = "Second Update";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondContext.SaveChangesAsync());
    }

    [Fact]
    public void EnumProperties_UseStringConversions()
    {
        AssertStringConversion<OrganizationMembership>(nameof(OrganizationMembership.Role));
        AssertStringConversion<OrganizationInvitation>(nameof(OrganizationInvitation.Role));
        AssertStringConversion<OrganizationInvitation>(nameof(OrganizationInvitation.Status));
    }

    [Fact]
    public void OrganizationMembership_HasCascadeOrganizationAndUserForeignKeys()
    {
        AssertForeignKey<OrganizationMembership, Organization>(
            [nameof(OrganizationMembership.OrganizationId)],
            DeleteBehavior.Cascade);
        AssertForeignKey<OrganizationMembership, ApplicationUser>(
            [nameof(OrganizationMembership.UserId)],
            DeleteBehavior.Cascade);
    }

    [Fact]
    public void ProjectMembership_HasExpectedForeignKeyDeleteBehaviors()
    {
        AssertForeignKey<ProjectMembership, Project>(
            [nameof(ProjectMembership.ProjectId)],
            DeleteBehavior.Cascade);
        AssertForeignKey<ProjectMembership, ApplicationUser>(
            [nameof(ProjectMembership.UserId)],
            DeleteBehavior.Cascade);
        AssertForeignKey<ProjectMembership, ApplicationUser>(
            [nameof(ProjectMembership.GrantedByUserId)],
            DeleteBehavior.Restrict);
    }

    [Fact]
    public void Invitation_HasRequiredFieldsForeignKeysAndIndexes()
    {
        var invitation = Entity<OrganizationInvitation>();

        Assert.Equal("OrganizationInvitations", invitation.GetTableName());
        AssertRequiredMaxLength(invitation, nameof(OrganizationInvitation.Email), 320);
        AssertRequiredMaxLength(invitation, nameof(OrganizationInvitation.NormalizedEmail), 320);
        AssertRequiredMaxLength(invitation, nameof(OrganizationInvitation.TokenHash), 64);
        Assert.False(invitation.FindProperty(nameof(OrganizationInvitation.InvitedByUserId))!.IsNullable);
        AssertForeignKey<OrganizationInvitation, Organization>(
            [nameof(OrganizationInvitation.OrganizationId)],
            DeleteBehavior.Cascade);
        AssertForeignKey<OrganizationInvitation, ApplicationUser>(
            [nameof(OrganizationInvitation.InvitedByUserId)],
            DeleteBehavior.Restrict);
        AssertIndex(
            invitation,
            [nameof(OrganizationInvitation.OrganizationId), nameof(OrganizationInvitation.NormalizedEmail), nameof(OrganizationInvitation.Status)],
            unique: false);
        AssertIndex(invitation, [nameof(OrganizationInvitation.TokenHash)], unique: true);
        AssertIndex(invitation, [nameof(OrganizationInvitation.ExpiresAt)], unique: false);
    }

    [Fact]
    public void Project_HasRequiredIndexedOrganizationForeignKeyWithRestrictedDelete()
    {
        var project = Entity<Project>();

        Assert.False(project.FindProperty(nameof(Project.OrganizationId))!.IsNullable);
        AssertIndex(project, [nameof(Project.OrganizationId)], unique: false);
        AssertForeignKey<Project, Organization>(
            [nameof(Project.OrganizationId)],
            DeleteBehavior.Restrict);
    }

    [Fact]
    public void ApplicationUser_DefaultSelectionsAreOptionalSetNullForeignKeys()
    {
        var user = Entity<ApplicationUser>();

        Assert.True(user.FindProperty("DefaultOrganizationId")!.IsNullable);
        Assert.True(user.FindProperty(nameof(ApplicationUser.DefaultProjectId))!.IsNullable);
        AssertForeignKey<ApplicationUser, Organization>(
            ["DefaultOrganizationId"],
            DeleteBehavior.SetNull);
        AssertForeignKey<ApplicationUser, Project>(
            [nameof(ApplicationUser.DefaultProjectId)],
            DeleteBehavior.SetNull);
    }

    [Fact]
    public void DashboardPreference_PreservesCompositeKeyAndHasUserAndProjectForeignKeys()
    {
        var preference = Entity<UserDashboardPreference>();

        Assert.Equal(
            [nameof(UserDashboardPreference.UserId), nameof(UserDashboardPreference.ProjectId)],
            preference.FindPrimaryKey()!.Properties.Select(property => property.Name));
        AssertForeignKey<UserDashboardPreference, ApplicationUser>(
            [nameof(UserDashboardPreference.UserId)],
            DeleteBehavior.Cascade);
        AssertForeignKey<UserDashboardPreference, Project>(
            [nameof(UserDashboardPreference.ProjectId)],
            DeleteBehavior.Cascade);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private IEntityType Entity<TEntity>() => _model.FindEntityType(typeof(TEntity))!;

    private static Organization CreateOrganization(string name) =>
        new()
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow
        };

    private static void AssertRequiredMaxLength(IEntityType entity, string propertyName, int maxLength)
    {
        var property = entity.FindProperty(propertyName)!;
        Assert.False(property.IsNullable);
        Assert.Equal(maxLength, property.GetMaxLength());
    }

    private void AssertStringConversion<TEntity>(string propertyName)
    {
        var property = Entity<TEntity>().FindProperty(propertyName)!;
        Assert.Equal(typeof(string), property.GetTypeMapping().Converter!.ProviderClrType);
    }

    private void AssertForeignKey<TEntity, TPrincipal>(
        IReadOnlyList<string> propertyNames,
        DeleteBehavior deleteBehavior)
    {
        var foreignKey = Assert.Single(
            Entity<TEntity>().GetForeignKeys(),
            candidate => candidate.PrincipalEntityType.ClrType == typeof(TPrincipal)
                && candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
        Assert.Equal(deleteBehavior, foreignKey.DeleteBehavior);
    }

    private static void AssertIndex(IEntityType entity, IReadOnlyList<string> propertyNames, bool unique)
    {
        var index = Assert.Single(
            entity.GetIndexes(),
            candidate => candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
        Assert.Equal(unique, index.IsUnique);
    }

    private static void AssertDbSet<TEntity>(string propertyName)
        where TEntity : class
    {
        var property = typeof(ScrumPilotContext).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(typeof(DbSet<TEntity>), property.PropertyType);
    }
}
