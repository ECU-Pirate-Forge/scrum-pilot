using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/organizations/{organizationId:int}/projects/{projectId:int}/tasks/{taskId:int}/specifications")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class TaskSpecificationsController(ICurrentUser user, IOrganizationAccessService access,
    OrganizationContext db, TaskSpecificationService specs, PullRequestScopeService scope,
    ProjectWorkNotifier notifier) : ProjectPlanningControllerBase(user, access)
{
    [HttpGet]
    public async Task<IActionResult> Get(int projectId, int taskId, CancellationToken ct) =>
        !await CanRead(projectId, ct) ? NotFound() : Ok(await specs.StatusAsync(projectId, taskId, ct));

    [HttpGet("history")]
    public async Task<IActionResult> History(int projectId, int taskId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        return Ok(await db.TaskSpecificationRevisions.AsNoTracking().Include(item => item.AllowedFiles)
            .Where(item => item.ProjectId == projectId && item.PbiId == taskId).OrderByDescending(item => item.Revision).ToListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Save(int projectId, int taskId, SaveTaskSpecificationRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        await scope.InvalidateTaskAsync(projectId, taskId, ct);
        var saved = await specs.SaveAsync(projectId, taskId, request, Actor, ct);
        await transaction.CommitAsync(ct);
        await notifier.NotifyAsync(projectId, await db.ProjectGraphStates.Where(item => item.ProjectId == projectId)
            .Select(item => item.Revision).SingleAsync(ct), ct);
        return Ok(saved);
    }

    [HttpPost("approve")]
    public async Task<IActionResult> Approve(int projectId, int taskId, ApproveTaskSpecificationRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        if (!await CanManage(projectId, ct)) return Forbid();
        var approved = await specs.ApproveAsync(projectId, taskId, request.ExpectedRevision, Actor, ct);
        foreach (var binding in await db.TaskPullRequestBindings.Where(item => item.ProjectId == projectId && item.PbiId == taskId).ToListAsync(ct))
            await scope.ValidateAsync(binding, ct);
        return Ok(approved);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(int projectId, int taskId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        var status = await specs.StatusAsync(projectId, taskId, ct);
        var spec = status.Specification ?? throw new KeyNotFoundException();
        var snapshot = await db.RepositorySnapshots.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.RepositorySnapshotId == spec.RepositorySnapshotId, ct);
        var task = await db.Stories.SingleAsync(item => item.ProjectId == projectId && item.PbiId == taskId, ct);
        var dependencies = await db.PbiDependencies.Where(item => item.ProjectId == projectId
            && (item.DependentPbiId == taskId || item.DependentPbiId == task.ParentStoryId))
            .Select(item => item.PrerequisitePbiId).Distinct().Order().ToArrayAsync(ct);
        return Content(TaskSpecificationService.Markdown(spec, snapshot, dependencies), "text/markdown", System.Text.Encoding.UTF8);
    }

    [HttpGet("manifest")]
    public async Task<IActionResult> Manifest(int projectId, int taskId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        var status = await specs.StatusAsync(projectId, taskId, ct);
        var spec = status.Specification ?? throw new KeyNotFoundException();
        var snapshot = await db.RepositorySnapshots.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.RepositorySnapshotId == spec.RepositorySnapshotId, ct);
        return Ok(new
        {
            SchemaVersion = 1,
            ProjectId = projectId,
            TaskId = taskId,
            spec.Revision,
            spec.ContentHash,
            spec.Approval,
            status.IsCurrent,
            status.ReviewReasons,
            RepositorySha = snapshot?.CommitSha,
            snapshot?.Branch,
            Files = spec.AllowedFiles.Select(TaskSpecificationService.Proposal)
        });
    }
}
