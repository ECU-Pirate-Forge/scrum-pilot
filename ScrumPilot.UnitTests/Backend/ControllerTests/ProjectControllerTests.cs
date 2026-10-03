using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ControllerTests;

public sealed class ProjectControllerTests
{
    private readonly IProjectService _service = Substitute.For<IProjectService>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public ProjectControllerTests() => _currentUser.UserId.Returns("user");

    [Fact]
    public void Routes_ExposeOnlyOrganizationScopedListAndRequiredProjectEndpoints()
    {
        var routes = typeof(ProjectController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
                .Select(attribute => (
                    Method: attribute.HttpMethods.Single(),
                    Template: attribute.Template ?? string.Empty)))
            .ToHashSet();

        Assert.Contains(("GET", "organizations/{organizationId:int}/projects"), routes);
        Assert.Contains(("GET", "projects/{projectId:int}"), routes);
        Assert.Contains(("POST", "organizations/{organizationId:int}/projects"), routes);
        Assert.Contains(("PUT", "projects/{projectId:int}"), routes);
        Assert.Contains(("DELETE", "projects/{projectId:int}"), routes);
        Assert.Contains(("GET", "projects/{projectId:int}/members"), routes);
        Assert.Contains(("PUT", "projects/{projectId:int}/members/{userId}"), routes);
        Assert.Contains(("DELETE", "projects/{projectId:int}/members/{userId}"), routes);
        Assert.DoesNotContain(("GET", string.Empty), routes);
    }

    [Fact]
    public async Task Get_InaccessibleProject_ReturnsNotFound()
    {
        var controller = CreateController();
        _service.GetAccessibleProjectAsync("user", 42, Arg.Any<CancellationToken>())
            .Returns<Task<Project>>(_ => throw new ProjectNotFoundException());

        var result = await controller.Get(42);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_NonOwner_ReturnsForbidden()
    {
        var controller = CreateController();
        _service.UpdateAsync("user", 10, Arg.Any<UpdateProjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<Project>>(_ => throw new ProjectForbiddenException());

        var result = await controller.Update(10, new("Name", null));

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCanonicalProjectLocation()
    {
        var controller = CreateController();
        var request = new CreateProjectRequest("Project", null);
        var project = new Project { ProjectId = 7, OrganizationId = 3, ProjectName = "Project" };
        _service.CreateAsync("user", 3, request, Arg.Any<CancellationToken>()).Returns(project);

        var result = await controller.Create(3, request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ProjectController.Get), created.ActionName);
        Assert.Equal(7, created.RouteValues!["projectId"]);
    }

    [Fact]
    public async Task PutMember_ConflictReturnsConflict()
    {
        var controller = CreateController();
        _service.SetAccessAsync(
                "user",
                10,
                "owner",
                Arg.Any<SetProjectAccessRequest>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new ProjectConflictException("Owners have implicit access."));

        var result = await controller.SetMemberAccess(10, "owner", new(true));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task DeleteMember_UsesRevokeRequest()
    {
        var controller = CreateController();

        var result = await controller.RemoveMemberAccess(10, "member");

        Assert.IsType<NoContentResult>(result);
        await _service.Received(1).SetAccessAsync(
            "user",
            10,
            "member",
            Arg.Is<SetProjectAccessRequest>(request => !request.HasAccess),
            Arg.Any<CancellationToken>());
    }

    private ProjectController CreateController(bool authenticated = true)
    {
        var identity = authenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "test")
            : new ClaimsIdentity();
        return new ProjectController(_service, _currentUser)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }
}
