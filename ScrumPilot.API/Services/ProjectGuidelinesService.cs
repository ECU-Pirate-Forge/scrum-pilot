using System.Text;
using System.Data;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class ProjectGuidelinesService(OrganizationContext db, PullRequestScopeService? scopes = null,
    ProjectWorkNotifier? notifier = null)
{
    public const int MaximumContentBytes = 128 * 1024;

    public static string Template()
    {
        using var stream = typeof(ProjectGuidelinesService).Assembly
            .GetManifestResourceStream("ScrumPilot.ProjectGuidelinesTemplate.md")
            ?? throw new InvalidOperationException("The canonical guidelines template is unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public Task<ProjectGuidelinesRevision?> CurrentAsync(int projectId, CancellationToken ct) =>
        (from p in db.Projects.AsNoTracking()
         join g in db.ProjectGuidelinesRevisions.AsNoTracking() on p.CurrentGuidelinesRevisionId equals g.ProjectGuidelinesRevisionId
         where p.ProjectId == projectId && g.ProjectId == projectId
         select g).SingleOrDefaultAsync(ct);

    public Task<List<ProjectGuidelinesRevision>> HistoryAsync(int projectId, CancellationToken ct) =>
        db.ProjectGuidelinesRevisions.AsNoTracking().Where(g => g.ProjectId == projectId)
            .OrderByDescending(g => g.Revision).ToListAsync(ct);

    public async Task<ProjectGuidelinesRevision> RestoreAsync(int projectId, int revisionId,
        RestoreProjectGuidelinesRequest request, string actor, CancellationToken ct)
    {
        var old = await db.ProjectGuidelinesRevisions.AsNoTracking()
            .SingleOrDefaultAsync(g => g.ProjectId == projectId && g.ProjectGuidelinesRevisionId == revisionId, ct)
            ?? throw new KeyNotFoundException();
        return await SaveAsync(projectId, new(old.Content, request.ExpectedRevisionId), actor, ct);
    }

    public async Task<ProjectGuidelinesRevision> SaveAsync(int projectId, SaveProjectGuidelinesRequest request,
        string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || Encoding.UTF8.GetByteCount(request.Content) > MaximumContentBytes)
            throw new ArgumentException("Guidelines must be non-empty and no larger than 128 KiB UTF-8.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct)
            ?? throw new KeyNotFoundException();
        if (project.CurrentGuidelinesRevisionId != request.ExpectedRevisionId)
            throw new DbUpdateConcurrencyException();
        var tasks = await db.Stories.AsNoTracking().Where(p => p.ProjectId == projectId && p.Type == PbiType.Task)
            .Select(p => p.PbiId).ToListAsync(ct);
        foreach (var taskId in tasks)
            await (scopes ?? throw new InvalidOperationException("Task check invalidation is unavailable; guidance was not changed."))
                .InvalidateTaskAsync(projectId, taskId, ct);
        var next = (await db.ProjectGuidelinesRevisions.Where(g => g.ProjectId == projectId)
            .MaxAsync(g => (int?)g.Revision, ct) ?? 0) + 1;
        var revision = new ProjectGuidelinesRevision
        {
            ProjectId = projectId,
            Revision = next,
            Content = request.Content,
            ActorId = actor
        };
        db.ProjectGuidelinesRevisions.Add(revision);
        try
        {
            await db.SaveChangesAsync(ct);
            var updated = await db.Projects.Where(p => p.ProjectId == projectId &&
                    p.CurrentGuidelinesRevisionId == request.ExpectedRevisionId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CurrentGuidelinesRevisionId,
                    (int?)revision.ProjectGuidelinesRevisionId), ct);
            if (updated != 1) throw new DbUpdateConcurrencyException();
            await tx.CommitAsync(ct);
            if (notifier is not null)
            {
                var graphRevision = await db.ProjectGraphStates.AsNoTracking().Where(s => s.ProjectId == projectId)
                    .Select(s => s.Revision).SingleOrDefaultAsync(ct);
                await notifier.NotifyAsync(projectId, graphRevision, ct);
            }
            return revision;
        }
        catch (DbUpdateException ex)
        {
            throw new DbUpdateConcurrencyException("Guidelines changed; reload before saving.", ex);
        }
    }
}
