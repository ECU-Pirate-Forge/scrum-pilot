using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;

namespace ScrumPilot.API.Controllers;

[OrganizationWork]
public abstract class ProjectPlanningControllerBase(
    ICurrentUser currentUser, IOrganizationAccessService access) : ControllerBase
{
    protected string Actor => currentUser.UserId;

    protected async Task<bool> CanRead(int projectId, CancellationToken ct) =>
        await access.CanAccessProjectAsync(Actor, projectId, ct);

    protected async Task<bool> CanManage(int projectId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return false;
        var organizationId = await access.GetOrganizationIdForProjectAsync(projectId, ct);
        return organizationId.HasValue && await access.IsOrganizationOwnerAsync(Actor, organizationId.Value, ct);
    }
}

public sealed class PlanningErrorFilter(ILogger<PlanningErrorFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, title) = context.Exception switch
        {
            ArgumentException => (400, "Invalid planning input"),
            KeyNotFoundException => (404, "Planning resource not found"),
            DbUpdateConcurrencyException => (409, "Planning data changed; reload and retry"),
            Microsoft.Data.SqlClient.SqlException { Number: 1205 or 1222 or 2601 or 2627 or 3960 or 51001 }
                or DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 1205 or 1222 or 2601 or 2627 or 3960 or 51001 } }
                => (409, "Planning data is being updated; reload and retry"),
            InvalidOperationException => (409, "Planning operation cannot be completed"),
            JsonException => (400, "The AI provider returned an invalid planning contract"),
            HttpRequestException => (502, "A planning integration is unavailable"),
            TaskCanceledException => (408, "The planning request was cancelled or timed out"),
            _ => (0, "")
        };
        if (status == 0) return;
        logger.LogWarning(context.Exception, "Planning operation failed with status {Status} for {Path}", status,
            context.HttpContext.Request.Path);
        var detail = context.Exception is ArgumentException or InvalidOperationException
            ? context.Exception.Message : title;
        context.Result = new ObjectResult(new ProblemDetails { Status = status, Title = title, Detail = detail })
        { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
