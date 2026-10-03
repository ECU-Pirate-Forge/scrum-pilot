using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

/// <summary>
/// Manages Project resources.
/// </summary>
[ApiController]
[Authorize]
[Route("api")]
public sealed class ProjectController(
    IProjectService service,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("organizations/{organizationId:int}/projects")]
    public Task<ActionResult<IReadOnlyList<Project>>> List(
        int organizationId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.GetAccessibleProjectsAsync(
                currentUser.UserId,
                organizationId,
                cancellationToken),
            value => Ok(value));

    [HttpGet("projects/{projectId:int}")]
    public Task<ActionResult<Project>> Get(
        int projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.GetAccessibleProjectAsync(
                currentUser.UserId,
                projectId,
                cancellationToken),
            value => Ok(value));

    [HttpPost("organizations/{organizationId:int}/projects")]
    public Task<ActionResult<Project>> Create(
        int organizationId,
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.CreateAsync(
                currentUser.UserId,
                organizationId,
                request,
                cancellationToken),
            value => CreatedAtAction(nameof(Get), new { projectId = value.ProjectId }, value));

    [HttpPut("projects/{projectId:int}")]
    public Task<ActionResult<Project>> Update(
        int projectId,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.UpdateAsync(
                currentUser.UserId,
                projectId,
                request,
                cancellationToken),
            value => Ok(value));

    [HttpDelete("projects/{projectId:int}")]
    public Task<IActionResult> Delete(
        int projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.DeleteAsync(currentUser.UserId, projectId, cancellationToken),
            NoContent);

    [HttpGet("projects/{projectId:int}/members")]
    public Task<ActionResult<IReadOnlyList<ProjectMemberAccessDto>>> GetMembers(
        int projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.GetMembersAsync(currentUser.UserId, projectId, cancellationToken),
            value => Ok(value));

    [HttpPut("projects/{projectId:int}/members/{userId}")]
    public Task<IActionResult> SetMemberAccess(
        int projectId,
        string userId,
        [FromBody] SetProjectAccessRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.SetAccessAsync(
                currentUser.UserId,
                projectId,
                userId,
                request,
                cancellationToken),
            NoContent);

    [HttpDelete("projects/{projectId:int}/members/{userId}")]
    public Task<IActionResult> RemoveMemberAccess(
        int projectId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            () => service.SetAccessAsync(
                currentUser.UserId,
                projectId,
                userId,
                new SetProjectAccessRequest(false),
                cancellationToken),
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
        catch (ProjectApplicationException exception)
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
        catch (ProjectApplicationException exception)
        {
            return Map(exception);
        }
    }

    private ActionResult<T> Map<T>(ProjectApplicationException exception) => Map(exception);

    private ObjectResult Map(ProjectApplicationException exception)
    {
        var status = exception switch
        {
            ProjectValidationException => StatusCodes.Status400BadRequest,
            ProjectForbiddenException => StatusCodes.Status403Forbidden,
            ProjectNotFoundException => StatusCodes.Status404NotFound,
            ProjectConflictException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Invalid project request",
                StatusCodes.Status403Forbidden => "Forbidden",
                StatusCodes.Status404NotFound => "Project not found",
                StatusCodes.Status409Conflict => "Project conflict",
                _ => "Project operation failed"
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
