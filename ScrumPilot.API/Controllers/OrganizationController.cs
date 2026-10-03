using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations")]
public sealed class OrganizationController(IOrganizationService service) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<IReadOnlyList<OrganizationSummaryDto>>> List(
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.ListAsync(cancellationToken),
            value => Ok(value));

    [HttpPost]
    public Task<ActionResult<OrganizationCreatedDto>> Create(
        [FromBody] CreateOrganizationRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.CreateAsync(request, cancellationToken),
            value => StatusCode(StatusCodes.Status201Created, value));

    [HttpGet("{organizationId:int}")]
    public Task<ActionResult<OrganizationSummaryDto>> Get(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.GetAsync(organizationId, cancellationToken),
            value => Ok(value));

    [HttpPut("{organizationId:int}")]
    public Task<ActionResult<OrganizationSummaryDto>> Rename(
        int organizationId,
        [FromBody] RenameOrganizationRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.RenameAsync(organizationId, request, cancellationToken),
            value => Ok(value));

    [HttpDelete("{organizationId:int}")]
    public Task<IActionResult> Delete(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.DeleteAsync(organizationId, cancellationToken),
            NoContent);

    [HttpPost("{organizationId:int}/restore")]
    public Task<ActionResult<OrganizationSummaryDto>> Restore(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.RestoreAsync(organizationId, cancellationToken),
            value => Ok(value));

    [HttpDelete("{organizationId:int}/purge")]
    public Task<IActionResult> Purge(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.PurgeAsync(organizationId, cancellationToken),
            NoContent);

    [HttpGet("{organizationId:int}/members")]
    public Task<ActionResult<IReadOnlyList<OrganizationMemberDto>>> ListMembers(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.ListMembersAsync(organizationId, cancellationToken),
            value => Ok(value));

    [HttpPut("{organizationId:int}/members/{userId}/role")]
    public Task<IActionResult> UpdateMemberRole(
        int organizationId,
        string userId,
        [FromBody] UpdateOrganizationMemberRoleRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.UpdateMemberRoleAsync(
                organizationId,
                userId,
                request,
                cancellationToken),
            NoContent);

    [HttpDelete("{organizationId:int}/members/{userId}")]
    public Task<IActionResult> RemoveMember(
        int organizationId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.RemoveMemberAsync(organizationId, userId, cancellationToken),
            NoContent);

    [HttpPost("{organizationId:int}/leave")]
    public Task<IActionResult> Leave(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.LeaveAsync(organizationId, cancellationToken),
            NoContent);

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
        catch (OrganizationApplicationException exception)
        {
            return Map<T>(exception);
        }
    }

    private async Task<IActionResult> ExecuteAsync(
        Func<Task> action,
        Func<IActionResult> success)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized();
        }

        try
        {
            await action();
            return success();
        }
        catch (OrganizationApplicationException exception)
        {
            return Map(exception);
        }
    }

    private ActionResult<T> Map<T>(OrganizationApplicationException exception) =>
        Map(exception);

    private ObjectResult Map(OrganizationApplicationException exception)
    {
        var status = exception switch
        {
            OrganizationValidationException => StatusCodes.Status400BadRequest,
            OrganizationForbiddenException => StatusCodes.Status403Forbidden,
            OrganizationNotFoundException => StatusCodes.Status404NotFound,
            OrganizationConflictException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Invalid organization request",
                StatusCodes.Status403Forbidden => "Forbidden",
                StatusCodes.Status404NotFound => "Organization not found",
                StatusCodes.Status409Conflict => "Organization conflict",
                _ => "Organization operation failed"
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
