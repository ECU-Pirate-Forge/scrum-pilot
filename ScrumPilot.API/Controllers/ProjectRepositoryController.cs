using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[GitHubIntegration]
[Route("api/organizations/{organizationId:int}/projects/{projectId:int}/repository")]
[ServiceFilter(typeof(PlanningErrorFilter))]
[ServiceFilter(typeof(GitHubFeatureFilter))]
public sealed class ProjectRepositoryController(ICurrentUser user, IOrganizationAccessService access,
    OrganizationContext db, GitHubAppClient github, RepositoryContextService context, ControlPlaneContext control,
    GitHubRepositoryRegistry registry, GitHubCheckRecovery recovery) : ProjectPlanningControllerBase(user, access)
{
    [HttpGet]
    public async Task<IActionResult> Get(int projectId, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        var repository = await db.ProjectRepositories.AsNoTracking().SingleOrDefaultAsync(item => item.ProjectId == projectId, ct);
        var status = repository is not null && !await registry.IsCurrentAsync(repository, ct)
            ? "Suspended: an Owner must reconcile repository configuration and external checks before approval."
            : "Unverified: repository branch protection requires administrator confirmation.";
        return Ok(new ProjectRepositoryStatus(repository, github.IsConfigured, status, github.IsWebhookConfigured));
    }

    [HttpPut]
    public async Task<IActionResult> Configure(int projectId, ConfigureProjectRepositoryRequest request, CancellationToken ct)
    {
        if (!await CanRead(projectId, ct)) return NotFound();
        if (!await CanManage(projectId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Owner) || string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.DefaultBranch) || request.GitHubRepositoryId <= 0 || request.InstallationId <= 0)
            throw new ArgumentException("Owner, repository name, branch, and positive repository/installation IDs are required.");
        await github.GetTreeAsync(request.InstallationId, request.GitHubRepositoryId, request.Owner, request.Name, request.DefaultBranch, ct);
        if (!await CanManage(projectId, ct)) return NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        if (await db.ProjectRepositories.AnyAsync(item => item.GitHubRepositoryId == request.GitHubRepositoryId
            && item.ProjectId != projectId, ct))
            throw new InvalidOperationException("This GitHub repository already belongs to another project's required-check policy.");
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
                || await control.PullRequestScopeAttempts.AnyAsync(item => item.RegistrationId == existing.RegistrationId, ct)
                || await db.TaskPullRequestBindings.AnyAsync(item => item.ProjectRepositoryId == existing.ProjectRepositoryId, ct)
                || await db.NonTaskPullRequestExceptions.AnyAsync(item => item.ProjectRepositoryId == existing.ProjectRepositoryId, ct)))
            throw new InvalidOperationException("This repository has approved planning history. Replacing it requires a separately reviewed project migration.");
        var registration = await registry.ReserveAsync(projectId, request, ct);
        if (existing.GitHubRepositoryId != 0 && existing.GitHubRepositoryId != request.GitHubRepositoryId)
            await registry.RetireAsync(existing, ct);
        await recovery.InvalidateAsync(registration,
            "Repository configuration is being reconciled. Revalidate each approved task before merging.", ct);
        if (!await CanManage(projectId, ct)) throw new InvalidOperationException("Project access changed during repository configuration.");
        existing.RegistrationId = registration.RegistrationId;
        existing.GitHubRepositoryId = request.GitHubRepositoryId;
        existing.InstallationId = request.InstallationId;
        existing.Owner = request.Owner;
        existing.Name = request.Name;
        existing.DefaultBranch = request.DefaultBranch;
        existing.Version = checked(existing.Version + 1);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await registry.ActivateAsync(registration, existing, ct);
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
