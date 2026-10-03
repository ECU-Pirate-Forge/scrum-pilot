using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Shared;

public sealed class ProjectAccessContractTests
{
    [Fact]
    public void ProjectMutationRequests_DoNotExposeOrganizationId()
    {
        Assert.Null(typeof(CreateProjectRequest).GetProperty("OrganizationId"));
        Assert.Null(typeof(UpdateProjectRequest).GetProperty("OrganizationId"));
    }

    [Fact]
    public void SetProjectAccessRequest_DoesNotDuplicateRouteUserId()
    {
        Assert.Null(typeof(SetProjectAccessRequest).GetProperty("UserId"));
        Assert.NotNull(typeof(SetProjectAccessRequest).GetProperty("HasAccess"));
    }
}
