using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/projects/{projectId:int}/repository")]
[ServiceFilter(typeof(PlanningErrorFilter))]
public sealed class ProjectRepositoryController(ICurrentUser user, IOrganizationAccessService access,
    ScrumPilotContext db, GitHubAppClient github, RepositoryContextService context) : ProjectPlanningControllerBase(user, access)
{
    [HttpGet]
    public async Task<IActionResult> Get(int projectId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        return Ok(new ProjectRepositoryStatus(await db.ProjectRepositories.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId, ct), github.IsConfigured,
            "Unverified: repository branch protection requires administrator confirmation.", github.IsWebhookConfigured));
    }

    [HttpPut]
    public async Task<IActionResult> Configure(int projectId, ConfigureProjectRepositoryRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        if (!await CanManage(projectId, ct)) return Forbid();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        if (string.IsNullOrWhiteSpace(request.Owner) || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.DefaultBranch)) throw new ArgumentException("Owner, repository name and branch are required.");
        if (await db.ProjectRepositories.AnyAsync(item => item.GitHubRepositoryId == request.GitHubRepositoryId
            && item.ProjectId != projectId, ct))
            throw new InvalidOperationException("This GitHub repository already belongs to another project's required-check policy.");
        await github.GetTreeAsync(request.InstallationId, request.GitHubRepositoryId, request.Owner, request.Name, request.DefaultBranch, ct);
        var existing = await db.ProjectRepositories.SingleOrDefaultAsync(item => item.ProjectId == projectId, ct);
        if (existing is not null && existing.Version != request.ExpectedVersion)
            throw new InvalidOperationException("Repository settings changed. Reload before saving.");
        if (existing is null)
        {
            existing = new() { ProjectId = projectId, Owner = request.Owner, Name = request.Name, DefaultBranch = request.DefaultBranch };
            db.ProjectRepositories.Add(existing);
        }
        else if (existing.GitHubRepositoryId != request.GitHubRepositoryId
            && (await db.RepositorySnapshots.AnyAsync(item => item.ProjectRepositoryId == existing.ProjectRepositoryId, ct)
                || await db.PullRequestScopeAttempts.AnyAsync(item => item.ProjectRepositoryId == existing.ProjectRepositoryId, ct)
                || await db.NonTaskPullRequestExceptions.AnyAsync(item => item.ProjectRepositoryId == existing.ProjectRepositoryId, ct)))
            throw new InvalidOperationException("This repository has approved planning history. Replacing it requires a separately reviewed project migration.");
        existing.GitHubRepositoryId = request.GitHubRepositoryId;
        existing.InstallationId = request.InstallationId;
        existing.Owner = request.Owner;
        existing.Name = request.Name;
        existing.DefaultBranch = request.DefaultBranch;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(existing);
    }

    [HttpPost("snapshots")]
    public async Task<IActionResult> Snapshot(int projectId, CreateRepositorySnapshotRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        return Ok(await context.SnapshotAsync(projectId, request.Branch, ct));
    }

    [HttpGet("snapshots")]
    public async Task<IActionResult> Snapshots(int projectId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        return Ok(await db.RepositorySnapshots.AsNoTracking().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt).ToListAsync(ct));
    }
}
