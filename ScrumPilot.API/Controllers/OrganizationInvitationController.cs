using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:int}/invitations")]
public sealed class OrganizationInvitationController(
    IOrganizationInvitationService service) : ControllerBase
{
    [HttpPost]
    public Task<ActionResult<OrganizationInvitationDto>> Invite(
        int organizationId,
        [FromBody] InviteOrganizationMemberRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.InviteAsync(organizationId, request, cancellationToken),
            value => StatusCode(StatusCodes.Status201Created, value));

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<OrganizationInvitationDto>>> List(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.ListAsync(organizationId, cancellationToken),
            value => Ok(value));

    [HttpPost("{invitationId:int}/resend")]
    public Task<ActionResult<OrganizationInvitationDto>> Resend(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.ResendAsync(
                organizationId,
                invitationId,
                cancellationToken),
            value => Ok(value));

    [HttpDelete("{invitationId:int}")]
    public Task<IActionResult> Revoke(
        int organizationId,
        int invitationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.RevokeAsync(
                organizationId,
                invitationId,
                cancellationToken));

    [HttpPost("~/api/organization-invitations/accept")]
    public Task<ActionResult<AcceptedOrganizationDto>> Accept(
        [FromBody] AcceptOrganizationInvitationRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.AcceptAsync(request, cancellationToken),
            value => Ok(value));

    private async Task<ActionResult<T>> ExecuteAsync<T>(
        Func<Task<T>> action,
        Func<T, ActionResult<T>> success)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }
        try
        {
            return success(await action());
        }
        catch (Exception exception) when (
            exception is OrganizationApplicationException or InvitationDeliveryException)
        {
            return Map<T>(exception);
        }
    }

    private async Task<IActionResult> ExecuteAsync(Func<Task> action)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }
        try
        {
            await action();
            return NoContent();
        }
        catch (Exception exception) when (
            exception is OrganizationApplicationException or InvitationDeliveryException)
        {
            return Map(exception);
        }
    }

    private ActionResult<T> Map<T>(Exception exception) => Map(exception);

    private ObjectResult Map(Exception exception)
    {
        var status = exception switch
        {
            OrganizationValidationException => StatusCodes.Status400BadRequest,
            OrganizationForbiddenException => StatusCodes.Status403Forbidden,
            OrganizationNotFoundException => StatusCodes.Status404NotFound,
            OrganizationConflictException => StatusCodes.Status409Conflict,
            InvitationDeliveryException => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Invalid invitation request",
                StatusCodes.Status403Forbidden => "Forbidden",
                StatusCodes.Status404NotFound => "Invitation not found",
                StatusCodes.Status409Conflict => "Invitation conflict",
                StatusCodes.Status502BadGateway => "Invitation delivery failed",
                _ => "Invitation operation failed"
            },
            Detail = exception.Message
        };
        return status switch
        {
            StatusCodes.Status400BadRequest => BadRequest(problem),
            StatusCodes.Status404NotFound => NotFound(problem),
            StatusCodes.Status409Conflict => Conflict(problem),
            _ => StatusCode(status, problem)
        };
    }
}
