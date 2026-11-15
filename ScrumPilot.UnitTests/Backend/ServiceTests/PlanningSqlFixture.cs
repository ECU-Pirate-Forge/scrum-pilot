using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Models;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;
using ScrumPilot.UnitTests.Backend.Tenancy;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

/// <summary>Fixture-owned SQL Server 2025 catalogs; never substitutes SQLite for planning guarantees.</summary>
internal sealed class PlanningSqlFixture : IAsyncDisposable
{
    private readonly SqlTestDatabase files = new();
    public ControlPlaneContext Control => files.Control;
    public Organization Organization { get; private set; } = default!;
    public TenantSelection Selection { get; } = new();
    public GitHubRepositoryRegistry Registry => new(Control, Selection);
    public OrganizationContext Db { get; private set; } = default!;
    public IOrganizationAccessService Access { get; private set; } = default!;
    public PlanningInferenceSession Inference { get; private set; } = default!;

    public static async Task<PlanningSqlFixture> CreateAsync(params IInterceptor[] interceptors)
    {
        var fixture = new PlanningSqlFixture();
        fixture.Control.Users.Add(new ApplicationUser { Id = "owner", UserName = "owner", NormalizedUserName = "OWNER" });
        fixture.Organization = new() { Name = "Planning", NormalizedName = "PLANNING" };
        fixture.Control.Organizations.Add(fixture.Organization);
        await fixture.Control.SaveChangesAsync();
        fixture.Control.OrganizationMemberships.Add(new()
        {
            OrganizationId = fixture.Organization.OrganizationId,
            UserId = "owner",
            Role = OrganizationRole.Owner
        });
        await fixture.Control.SaveChangesAsync();
        await fixture.files.Operations.ProvisionAsync(fixture.Organization.OrganizationId);
        fixture.Selection.OrganizationId = fixture.Organization.OrganizationId;
        fixture.Db = fixture.NewContext(interceptors);
        var services = new ServiceCollection()
            .AddSingleton(fixture.Db)
            .AddSingleton<IProjectAccessRepository>(new TenantProjectAccessRepository(fixture.Db, fixture.Control, fixture.Selection))
            .BuildServiceProvider();
        fixture.Access = new TenantOrganizationAccessService(fixture.Control, services, fixture.Selection);
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns("owner");
        fixture.Inference = new(fixture.Db, fixture.files.Storage, fixture.Control, fixture.Selection, user, fixture.Access);
        return fixture;
    }

    public OrganizationContext NewContext(params IInterceptor[] interceptors)
    {
        using var trusted = files.Storage.Open(Organization);
        var context = new OrganizationContext(new DbContextOptionsBuilder<OrganizationContext>()
            .UseSqlServer(files.Storage.OrganizationConnectionString(Organization.StorageKey), sql => sql.UseCompatibilityLevel(170))
            .AddInterceptors(interceptors).Options);
        context.AdoptLease(trusted);
        return context;
    }

    public ControlPlaneContext NewControl() =>
        new(new DbContextOptionsBuilder<ControlPlaneContext>().UseSqlServer(Control.Database.GetConnectionString(),
            sql => sql.UseCompatibilityLevel(170)).Options);

    public async Task RegisterAsync(ScrumPilot.Shared.Models.ProjectRepository repository)
    {
        if (repository.RegistrationId == Guid.Empty) repository.RegistrationId = Guid.NewGuid();
        await Db.SaveChangesAsync();
        Control.GitHubRepositoryRegistrations.Add(new()
        {
            GitHubRepositoryId = repository.GitHubRepositoryId,
            RegistrationId = repository.RegistrationId,
            OrganizationId = Organization.OrganizationId,
            StorageKey = Organization.StorageKey,
            ProjectId = repository.ProjectId,
            ProjectRepositoryId = repository.ProjectRepositoryId,
            InstallationId = repository.InstallationId,
            Owner = repository.Owner,
            Name = repository.Name,
            ConfigurationVersion = repository.Version,
            State = RepositoryRegistrationState.Active
        });
        await Control.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        files.Dispose();
    }
}
