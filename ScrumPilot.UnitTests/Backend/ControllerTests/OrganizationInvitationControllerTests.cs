using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ScrumPilot.API.Controllers;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ControllerTests;

public sealed class OrganizationInvitationControllerTests
{
    private readonly IOrganizationInvitationService _service =
        Substitute.For<IOrganizationInvitationService>();

    [Fact]
    public async Task Invite_SuccessReturnsCreatedWithoutToken()
    {
        var controller = CreateController();
        var request = new InviteOrganizationMemberRequest(
            "member@example.com",
            OrganizationRole.Member);
        var dto = Dto();
        _service.InviteAsync(5, request, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await controller.Invite(5, request);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Same(dto, created.Value);
        Assert.Null(dto.GetType().GetProperty("TokenHash"));
        Assert.Null(dto.GetType().GetProperty("Token"));
    }

    [Fact]
    public async Task Invite_DeliveryFailureReturnsBadGateway()
    {
        var controller = CreateController();
        _service.InviteAsync(5, Arg.Any<InviteOrganizationMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<OrganizationInvitationDto>>(_ =>
                throw new InvitationDeliveryException("SendGrid returned HTTP status 503."));

        var result = await controller.Invite(
            5,
            new("member@example.com", OrganizationRole.Member));

        var failure = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, failure.StatusCode);
    }

    [Fact]
    public async Task Accept_ConflictReturnsConflict()
    {
        var controller = CreateController();
        _service.AcceptAsync(Arg.Any<AcceptOrganizationInvitationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OrganizationConflictException("Cannot accept."));

        var result = await controller.Accept(new("token"));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task List_UnauthenticatedReturnsUnauthorized()
    {
        var controller = CreateController(authenticated: false);

        var result = await controller.List(5);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    private OrganizationInvitationController CreateController(bool authenticated = true)
    {
        var identity = authenticated
            ? new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "owner"),
                    new Claim(ClaimTypes.Email, "owner@example.com")
                ],
                "test")
            : new ClaimsIdentity();
        return new(_service)
        {
            ControllerContext = new()
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static OrganizationInvitationDto Dto() => new(
        9,
        5,
        "member@example.com",
        "owner",
        OrganizationRole.Member,
        OrganizationInvitationStatus.Pending,
        DateTime.UtcNow,
        DateTime.UtcNow.AddHours(72),
        null,
        DateTime.UtcNow,
        null);
}
