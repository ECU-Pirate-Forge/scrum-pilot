using NSubstitute;
using ScrumPilot.API.Services;
using ScrumPilot.UnitTests.Backend.Tenancy;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class ProjectAccessServiceTests
{
    [Theory]
    [InlineData("owner", 1)]
    [InlineData("member", 1)]
    [InlineData("admin", 0)]
    public async Task Service_UsesCurrentTenantScopedAccess(string user, int expected)
    {
        using var fixture = new TenantAccessFixture();
        var service = new ProjectService(fixture.Repository, fixture.Access, TimeProvider.System,
            Substitute.For<IPlanningPokerConnectionEvictor>());
        if (expected == 0)
            await Assert.ThrowsAsync<ProjectNotFoundException>(() => service.GetAccessibleProjectsAsync(user, fixture.Organization.OrganizationId));
        else Assert.Equal(expected, (await service.GetAccessibleProjectsAsync(user, fixture.Organization.OrganizationId)).Count);
    }
}
