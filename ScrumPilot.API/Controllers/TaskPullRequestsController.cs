using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

public sealed record NonTaskExceptionRequest(int PullRequestNumber, string Reason);

[ApiController]
[GitHubIntegration]
[Route("api/organizations/{organizationId:int}/projects/{projectId:int}")]
[ServiceFilter(typeof(PlanningErrorFilter))]
[ServiceFilter(typeof(GitHubFeatureFilter))]
public sealed class TaskPullRequestsController(ICurrentUser user, IOrganizationAccessService access,
    OrganizationContext db, PullRequestScopeService scope, GitHubRepositoryRegistry registry) : ProjectPlanningControllerBase(user, access)
{
    [HttpPost("tasks/{taskId:int}/pull-requests")]
    public async Task<IActionResult> Bind(int projectId, int taskId, BindTaskPullRequestRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        if (!await CanManage(projectId, ct)) return Forbid();
        return Ok(await scope.BindAsync(projectId, taskId, request.PullRequestNumber, Actor, ct));
    }

    [HttpGet("tasks/{taskId:int}/pull-requests")]
    public async Task<IActionResult> Checks(int projectId, int taskId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        var ids = db.TaskPullRequestBindings.Where(item => item.ProjectId == projectId && item.PbiId == taskId)
            .Select(item => item.TaskPullRequestBindingId);
        return Ok(await db.PullRequestScopeChecks.AsNoTracking().Where(item => item.ProjectId == projectId
            && ids.Contains(item.TaskPullRequestBindingId)).OrderByDescending(item => item.CreatedAt).ToListAsync(ct));
    }

    [HttpPost("repository/non-task-exceptions")]
    public async Task<IActionResult> Exception(int projectId, NonTaskExceptionRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        if (!await CanManage(projectId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000 || request.PullRequestNumber <= 0)
            throw new ArgumentException("Provide a PR number and a clear reason of at most 2,000 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var repository = await db.ProjectRepositories.SingleAsync(item => item.ProjectId == projectId, ct);
        await registry.RequireCurrentAsync(repository, ct);
        if (await db.TaskPullRequestBindings.AnyAsync(item => item.ProjectRepositoryId == repository.ProjectRepositoryId
            && item.PullRequestNumber == request.PullRequestNumber, ct)) throw new InvalidOperationException("Task-bound PRs cannot bypass scope through non-task exceptions.");
        db.NonTaskPullRequestExceptions.Add(new()
        {
            ProjectId = projectId,
            ProjectRepositoryId = repository.ProjectRepositoryId,
            PullRequestNumber = request.PullRequestNumber,
            Reason = request.Reason,
            ActorId = Actor
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await scope.ProcessWebhookAsync(repository.GitHubRepositoryId, repository.InstallationId, request.PullRequestNumber, ct);
        return NoContent();
    }
}
