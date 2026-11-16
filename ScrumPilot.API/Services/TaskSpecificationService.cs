using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class TaskSpecificationService(OrganizationContext db, GitHubAppClient github, ProjectWorkNotifier notifier,
    GitHubRepositoryRegistry registry)
{
    public async Task<TaskSpecificationStatusDto> StatusAsync(int projectId, int taskId, CancellationToken ct)
    {
        var task = await TaskAsync(projectId, taskId, ct);

        var specification = await db.TaskSpecificationRevisions
                                    .AsNoTracking().Include(item => item.AllowedFiles)
                                    .Where(item => item.ProjectId == projectId && item.PbiId == taskId)
                                    .OrderByDescending(item => item.Revision)
                                    .FirstOrDefaultAsync(ct);

        if (specification is null) 
        {
            return new(null, false, ["No implementation specification has been saved."]);
        }

        var reasons = new List<string>();

        var project = await db.Projects
                                .AsNoTracking()
                                .SingleAsync(item => item.ProjectId == projectId, ct);

        if (specification.ProjectGuidelinesRevisionId != project.CurrentGuidelinesRevisionId)
        {
            reasons.Add("Project guidance changed; review and save a new specification revision.");
        }

        var source = await SourceAsync(task, ct);

        if (source.Type == PbiType.Story)
        {
            var currency = await ProjectStoryCurrency.ReadAsync(db, projectId, source.PbiId, ct);

            if (currency.SourceChanged || currency.GuidelinesChanged || currency.SourceArchived || currency.ContentChanged)
            {
                reasons.Add("The source story's problem statement or generation guidance requires Owner applicability review.");
            }
        }
        if (specification.Content.SourceStorySnapshot != SourceText(source))
        {
            reasons.Add("The source work requirements changed; review the specification.");
        }

        var snapshot = await db.RepositorySnapshots.SingleOrDefaultAsync(item => item.ProjectId == projectId
            && item.RepositorySnapshotId == specification.RepositorySnapshotId, ct);

        if (snapshot is null) 
        {
            reasons.Add("Repository paths have not been verified.");
        }
        else
        {
            var repository = await db.ProjectRepositories
                                    .AsNoTracking()
                                    .SingleOrDefaultAsync(item => item.ProjectId == projectId 
                                                                    && item.ProjectRepositoryId == snapshot.ProjectRepositoryId, 
                                    ct);

            if (repository is null || !await registry.IsCurrentAsync(repository, ct))
            {
                reasons.Add("Repository registration is suspended or changed; Owner review is required.");
            }
        }

        if (specification.Approval != SpecificationApproval.Approved) 
        {
            reasons.Add("Owner file-scope approval is required.");
        }

        return new(specification, reasons.Count == 0, reasons.ToArray());
    }

    public async Task<TaskSpecificationRevision> SaveAsync(int projectId, int taskId,
        SaveTaskSpecificationRequest request, string actor, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
            
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);

        var task = await TaskAsync(projectId, taskId, ct);

        var latest = await db.TaskSpecificationRevisions
                            .Where(item => item.ProjectId == projectId && item.PbiId == taskId)
                            .OrderByDescending(item => item.Revision)
                            .FirstOrDefaultAsync(ct);

        if ((latest?.Revision ?? 0) != request.ExpectedRevision)
        {
            throw new InvalidOperationException("The specification changed; reload before amending its scope.");
        }

        ValidateContent(request.Content);

        var snapshot = await db.RepositorySnapshots.SingleOrDefaultAsync(item => item.ProjectId == projectId
            && item.RepositorySnapshotId == request.RepositorySnapshotId, ct) ?? throw new KeyNotFoundException();

        var files = VerifyFiles(request.Files, snapshot);

        var repository = await db.ProjectRepositories.AsNoTracking().SingleAsync(item =>
            item.ProjectId == projectId && item.ProjectRepositoryId == snapshot.ProjectRepositoryId, ct);

        await registry.RequireCurrentAsync(repository, ct);

        var source = await SourceAsync(task, ct);

        var project = await db.Projects.AsNoTracking().SingleAsync(item => item.ProjectId == projectId, ct);

        var spec = new TaskSpecificationRevision
        {
            ProjectId = projectId,
            PbiId = taskId,
            Revision = request.ExpectedRevision + 1,
            ActorId = actor,
            Content = request.Content with { SourceStoryVersion = source.Version, SourceStorySnapshot = SourceText(source) },
            ProjectGuidelinesRevisionId = project.CurrentGuidelinesRevisionId,
            RepositorySnapshotId = snapshot.RepositorySnapshotId,
            ScopeMode = TaskScopeMode.Enforced,
            Approval = SpecificationApproval.Draft,
            GenerationRunId = task.GenerationRunId,
            AllowedFiles = files.Select(file => File(projectId, file)).ToList()
        };

        db.TaskSpecificationRevisions.Add(spec);

        var graph = await TouchGraphAsync(projectId, ct);

        await db.SaveChangesAsync(ct);

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            await notifier.NotifyAsync(projectId, graph.Revision, ct);
        }
        return spec;
    }

    public async Task<TaskSpecificationRevision> ApproveAsync(int projectId, int taskId, int expectedRevision,
        string actor, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct) : null;
        await db.AcquirePlanningPolicyLockAsync(projectId, ct);

        var status = await StatusAsync(projectId, taskId, ct);

        var previous = status.Specification ?? throw new KeyNotFoundException();
        
        if (previous.Revision != expectedRevision) 
        {
            throw new InvalidOperationException("The specification changed. Reload before approval.");
        }

        if (status.ReviewReasons.Any(reason => reason != "Owner file-scope approval is required."))
        {
            throw new InvalidOperationException(string.Join(" ", status.ReviewReasons));
        }

        if (previous.Approval == SpecificationApproval.Approved) 
        {
            return previous;
        }

        var snapshot = await db.RepositorySnapshots.SingleAsync(item => item.ProjectId == projectId
            && item.RepositorySnapshotId == previous.RepositorySnapshotId, ct);

        var repository = await db.ProjectRepositories.SingleAsync(item => item.ProjectId == projectId
            && item.ProjectRepositoryId == snapshot.ProjectRepositoryId, ct);

        var current = await github.GetTreeAsync(repository.InstallationId, repository.GitHubRepositoryId,
            repository.Owner, repository.Name, snapshot.Branch, ct);

        if (current.CommitSha != snapshot.CommitSha)
        {
            throw new InvalidOperationException("The selected repository branch moved. Create a fresh snapshot and review a new specification revision.");
        }

        var verified = VerifyFiles(previous.AllowedFiles.Select(Proposal).ToArray(), snapshot);

        var approved = new TaskSpecificationRevision
        {
            ProjectId = projectId,
            PbiId = taskId,
            Revision = previous.Revision + 1,
            ActorId = actor,
            Content = previous.Content,
            ScopeMode = TaskScopeMode.Enforced,
            Approval = SpecificationApproval.Approved,
            ApprovedByActorId = actor,
            ApprovedAt = DateTime.UtcNow,
            RepositorySnapshotId = previous.RepositorySnapshotId,
            ProjectGuidelinesRevisionId = previous.ProjectGuidelinesRevisionId,
            GenerationRunId = previous.GenerationRunId,
            AllowedFiles = verified.Select(file => File(projectId, file)).ToList()
        };

        db.TaskSpecificationRevisions.Add(approved);

        var graph = await TouchGraphAsync(projectId, ct);

        await db.SaveChangesAsync(ct);

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            await notifier.NotifyAsync(projectId, graph.Revision, ct);
        }

        return approved;
    }

    public static string Markdown(TaskSpecificationRevision spec, RepositorySnapshot? snapshot, IEnumerable<int>? prerequisites = null)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Task #{spec.PbiId}: {spec.Content.Goal}");
        text.AppendLine($"\nProject: {spec.ProjectId}; specification revision: {spec.Revision}; hash: {spec.ContentHash}");
        text.AppendLine($"Guidelines revision ID: {spec.ProjectGuidelinesRevisionId?.ToString() ?? "none"}; repository SHA: {snapshot?.CommitSha ?? "unverified"}; approval: {spec.Approval}");
        text.AppendLine($"Repository snapshot: {snapshot?.RepositorySnapshotId}; branch: {snapshot?.Branch}; generation run: {spec.GenerationRunId}");
        void Section(string title, IEnumerable<string> lines)
        {
            text.AppendLine($"\n## {title}");
            foreach (var line in lines) text.AppendLine($"- {line}");
        }
        Section("Objective and in-scope requirements", spec.Content.Requirements);
        Section("Acceptance criteria", spec.Content.AcceptanceCriteria);
        Section("Current context", [spec.Content.CurrentContext, spec.Content.SourceStorySnapshot]);
        Section("Implementation instructions", spec.Content.ImplementationSteps);

        Section("Approved file operations", spec.AllowedFiles
                                            .OrderBy(file => file.Path, StringComparer.Ordinal)
                                            .Select(file =>
                                                $"{file.Operation} `{file.Path}` ({file.Category}): {file.Rationale}" +
                                                (file.RenameFromPath is null ? "" : $" from `{file.RenameFromPath}`"))
        );

        Section("Tests and verification", spec.Content.VerificationSteps);
        
        Section("Dependencies", (prerequisites ?? []).Select(id => $"Prerequisite work item #{id}; consult its current completion and review state."));
        
        Section("Documentation", spec.Content.Documentation);
        
        Section("Out of scope", spec.Content.OutOfScope);
        
        Section("Definition of Done", spec.Content.DefinitionOfDone);
       
        Section("Agent handoff", [
            "Change only the exact approved implementation, test, and documentation paths and operations.",
            "Stop and request an Owner-approved scope amendment if another file is required. Do not silently broaden scope.",
            "Do not perform unrelated refactoring, formatting, dependency upgrades, or configuration changes.",
            "Report every changed path, exact validation commands actually run and results, documentation updates, and blockers.",
            "A scope manifest is not a filesystem sandbox or proof of correctness."
        ]);

        return text.ToString();
    }

    private async Task<ProjectGraphState> TouchGraphAsync(int projectId, CancellationToken ct)
    {
        var graph = await db.ProjectGraphStates.SingleOrDefaultAsync(item => item.ProjectId == projectId, ct);
        if (graph is null)
        {
            graph = new() { ProjectId = projectId };
            db.ProjectGraphStates.Add(graph);
        }
        else db.Entry(graph).Property(item => item.Revision).IsModified = true;
        return graph;
    }

    public static string SourceText(ProductBacklogItem item) => JsonSerializer.Serialize(new
    { item.PbiId, item.Title, item.Description, item.ProblemStatementId, item.GenerationRunId });

    public static TaskFileProposal Proposal(TaskAllowedFile file) => new(file.Path, file.Category, file.Operation, file.Rationale, file.RenameFromPath);
    
    public static FileScopeRule Rule(TaskFileProposal file) => new(file.Path, file.Operation switch
    { AllowedFileOperation.Add => "create", AllowedFileOperation.Modify => "modify", AllowedFileOperation.Delete => "delete", AllowedFileOperation.Rename => "rename", _ => "invalid" },
        file.Category switch { AllowedFileCategory.Test => "test", AllowedFileCategory.Documentation => "documentation", _ => "implementation" },
        file.Rationale, file.RenameFromPath);
    
    public static TaskAllowedFile File(int projectId, TaskFileProposal file) => new()
    {
        ProjectId = projectId,
        Path = TaskFileScope.Normalize(file.Path),
        Category = file.Category,
        Operation = file.Operation,
        Rationale = file.Rationale,
        RenameFromPath = file.RenameFromPath is null ? null : TaskFileScope.Normalize(file.RenameFromPath)
    };

    public static TaskFileProposal[] VerifyFiles(TaskFileProposal[] files, RepositorySnapshot snapshot)
    {
        if (files is null || files.Any(file => file is null || !Enum.IsDefined(file.Category) || !Enum.IsDefined(file.Operation)))
        {
            throw new ArgumentException("Supply valid, explicitly categorized file operations.");
        }

        TaskFileScope.Validate(files.Select(Rule), RepositoryContextService.ReadTree(snapshot).Entries);

        if (!files.Any(file => file.Category == AllowedFileCategory.Test))
        {
            throw new ArgumentException("Include the specific associated test files in the implementation scope.");
        }

        return files;
    }

    public static void ValidateContent(TaskSpecificationContent content)
    {
        if (content is null || content.SchemaVersion != 1 || string.IsNullOrWhiteSpace(content.Goal) || content.Goal.Length > 2000
            || new[] { content.Requirements, content.AcceptanceCriteria, content.ImplementationSteps,
                content.VerificationSteps, content.Documentation, content.DefinitionOfDone, content.OutOfScope }
                .Any(lines => lines is null || lines.Length == 0 || lines.Any(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException("A specification requires a goal, requirements, acceptance criteria, implementation steps, exact test commands, documentation, Definition of Done, and exclusions.");
        }

        if (string.IsNullOrWhiteSpace(content.CurrentContext))
        {
            throw new ArgumentException("Explain the verified implementation context.");
        }
    }

    private async Task<ProductBacklogItem> TaskAsync(int projectId, int taskId, CancellationToken ct) =>
        await db.Stories.AsNoTracking().SingleOrDefaultAsync(item => item.ProjectId == projectId && item.PbiId == taskId && item.Type == PbiType.Task, ct)
        ?? throw new KeyNotFoundException();

    private async Task<ProductBacklogItem> SourceAsync(ProductBacklogItem task, CancellationToken ct) =>
        task.ParentStoryId.HasValue
            ? await db.Stories.AsNoTracking().SingleAsync(item => item.ProjectId == task.ProjectId && item.PbiId == task.ParentStoryId, ct) : task;
}
