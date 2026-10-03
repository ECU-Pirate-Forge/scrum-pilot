using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ControllerTests;

public sealed class OrganizationControllerTests
{
    private readonly IOrganizationService _service = Substitute.For<IOrganizationService>();

    [Fact]
    public async Task List_Unauthenticated_ReturnsUnauthorized()
    {
        var controller = CreateController(authenticated: false);

        var result = await controller.List();

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task Create_WhenCreatorDiffersFromOwner_ReturnsTruthfulCreatedBodyWithoutLocation()
    {
        var controller = CreateController();
        var request = new CreateOrganizationRequest("Pirate Forge", "initial-owner");
        var dto = new OrganizationCreatedDto(7, "Pirate Forge", "initial-owner");
        _service.CreateAsync(request, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await controller.Create(request);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Same(dto, created.Value);
        Assert.Null(created.GetType().GetProperty("Location"));
        Assert.Null(typeof(OrganizationCreatedDto).GetProperty("Role"));
    }

    [Fact]
    public async Task Rename_ConflictException_ReturnsConflictProblem()
    {
        var controller = CreateController();
        _service.RenameAsync(3, Arg.Any<RenameOrganizationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<OrganizationSummaryDto>>(_ =>
                throw new OrganizationConflictException("That organization name is already reserved."));

        var result = await controller.Rename(3, new("Taken"));

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Contains("reserved", problem.Detail);
    }

    [Fact]
    public async Task Members_InaccessibleException_ReturnsNotFound()
    {
        var controller = CreateController();
        _service.ListMembersAsync(99, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<OrganizationMemberDto>>>(_ =>
                throw new OrganizationNotFoundException());

        var result = await controller.ListMembers(99);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemoveMember_ForbiddenException_ReturnsForbidden()
    {
        var controller = CreateController();
        _service.RemoveMemberAsync(1, "target", Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OrganizationForbiddenException());

        var result = await controller.RemoveMember(1, "target");

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task Rename_ValidationException_ReturnsBadRequest()
    {
        var controller = CreateController();
        _service.RenameAsync(1, Arg.Any<RenameOrganizationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<OrganizationSummaryDto>>(_ =>
                throw new OrganizationValidationException("Name is required."));

        var result = await controller.Rename(1, new(""));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private OrganizationController CreateController(bool authenticated = true)
    {
        var identity = authenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "test")
            : new ClaimsIdentity();
        return new OrganizationController(_service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            }
        };
    }
}
