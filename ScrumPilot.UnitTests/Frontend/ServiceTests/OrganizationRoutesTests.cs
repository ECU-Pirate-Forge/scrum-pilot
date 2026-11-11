using ScrumPilot.Web.Services;
using ScrumPilot.Client;

namespace ScrumPilot.UnitTests.Frontend.ServiceTests;

public class OrganizationRoutesTests
{
    [Theory]
    [InlineData(1, "backlog", 7, "organizations/1/backlog?projectId=7")]
    [InlineData(2, "backlog", 7, "organizations/2/backlog?projectId=7")]
    [InlineData(3, "organization-management", null, "organizations/3/organization-management")]
    public void PagesAlwaysIncludeTheOrganization(int organization, string page, int? project, string expected) =>
        Assert.Equal(expected, OrganizationRoutes.Page(organization, page, project));

    [Theory]
    [InlineData("api/Pbi/getNonDraftPbis?projectId=1", "api/organizations/12/Pbi/getNonDraftPbis?projectId=1")]
    [InlineData("projects/1/members/user", "api/organizations/12/projects/1/members/user")]
    [InlineData("user/all?projectId=1", "api/organizations/12/user/all?projectId=1")]
    public void WorkApiPreservesSuffixButRequiresExplicitOrganization(string suffix, string expected) =>
        Assert.Equal(expected, OrganizationRoutes.Api(12, suffix));

    [Fact]
    public void InvalidOrganizationAndAlreadyQualifiedPathsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OrganizationRoutes.Api(0, "project"));
        Assert.Throws<ArgumentException>(() => OrganizationRoutes.Api(1, "api/organizations/2/projects"));
        Assert.Throws<ArgumentException>(() => OrganizationRoutes.Api(1, "https://example.com/projects"));
    }

    [Fact]
    public void PokerHubIncludesOrganization() =>
        Assert.Equal("hubs/organizations/8/planning-poker", OrganizationRoutes.PokerHub(8));

    [Theory]
    [InlineData("https://localhost/backlog?projectId=1")]
    [InlineData("https://localhost/planning-poker?projectId=1")]
    public void ProjectOnlyLinksCannotIdentifyAnOrganization(string url)
    {
        var uri = new Uri(url);
        Assert.Null(OrganizationRoutes.OrganizationId(uri));
        Assert.True(OrganizationRoutes.IsLegacyWorkLink(uri));
    }

    [Fact]
    public void ExplicitRouteSeparatesTenantFromLocalProjectId()
    {
        var uri = new Uri("https://localhost/organizations/8/backlog?projectId=1");
        Assert.Equal(8, OrganizationRoutes.OrganizationId(uri));
        Assert.Equal(1, OrganizationRoutes.ProjectId(uri));
        Assert.False(OrganizationRoutes.IsLegacyWorkLink(uri));
    }
}
