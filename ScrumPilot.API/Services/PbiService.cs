using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Services.AI;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class PbiService(IStoryGenerationService generator, IPbiRepository repository,
    OrganizationContext? tenant = null, PlanningInferenceSession? inference = null,
    LocalPlanningAi? localAi = null) : IPbiService
{
    public const int MaxGenerationBatchSize = 10;
    public const int MaxProblemStatementLength = 10000;
    public Task<IEnumerable<ProductBacklogItem>> GetFilteredPbisAsync(int? sprintId, int? epicId, int? projectId = null, CancellationToken cancellationToken = default) =>
        repository.GetFilteredPbisAsync(sprintId, epicId, projectId, cancellationToken);

    public Task<List<ProductBacklogItem>> GenerateAiPbis(List<string> problemStatements, CancellationToken cancellationToken = default)
    {
        RequireInferenceSession();
        return inference is null ? generator.GenerateAsync(problemStatements, cancellationToken)
            : inference.RunAsync(null, () => generator.GenerateAsync(problemStatements, cancellationToken), cancellationToken);
    }

    public async Task<ProductBacklogItem> ImprovePbiAsync(ProductBacklogItem pbi, CancellationToken cancellationToken = default)
    {
        RequireInferenceSession();
        var guidance = tenant is not null && pbi.ProjectId > 0
            ? await new ProjectGuidelinesService(tenant).CurrentAsync(pbi.ProjectId, cancellationToken) : null;
        var sourceVersion = pbi.Version;
        async Task<ProductBacklogItem> Improve()
        {
            if (guidance is null) return await generator.ImproveAsync(pbi, cancellationToken);
            if (localAi is null) throw new InvalidOperationException("Local planning AI is required for saved project guidance.");
            var output = await localAi.GenerateAsync<ProjectStoryAiOutput>("""
                Improve this backlog item's title and description using the project guidelines.
                Preserve the item's intent. The description, including acceptance criteria, must not exceed 2000 characters.
                Return exactly one story in {"stories":[{"title":"improved title","description":"user story and observable acceptance criteria","priority":"Medium","storyPoints":"Zero"}]}.
                Priority is None, Low, Medium or High. StoryPoints is Zero, One, Two, Three, Five, Eight, Thirteen or TwentyOne.
                Do not output identifiers or additional properties.
                """, new { pbi.Title, pbi.Description, pbi.Type, pbi.Priority, pbi.StoryPoints, Guidelines = guidance.Content }, cancellationToken);
            if (output.Stories is null || output.Stories.Count != 1)
                throw new InvalidOperationException("AI improvement must return exactly one item.");
            var improved = output.Stories[0];
            if (string.IsNullOrWhiteSpace(improved.Title) || improved.Title.Length > 200 ||
                string.IsNullOrWhiteSpace(improved.Description) || improved.Description.Length > 2000 ||
                !Enum.IsDefined(improved.Priority) || !Enum.IsDefined(improved.StoryPoints))
                throw new InvalidOperationException("Local AI returned invalid improvement content.");
            pbi.Title = improved.Title;
            pbi.Description = improved.Description;
            pbi.Priority = improved.Priority;
            pbi.StoryPoints = improved.StoryPoints;
            return pbi;
        }
        var result = inference is null ? await Improve()
            : await inference.RunAsync(pbi.ProjectId, Improve, cancellationToken);
        if (tenant is not null && pbi.PbiId > 0)
        {
            var current = await tenant.Stories.AsNoTracking().SingleOrDefaultAsync(x =>
                x.PbiId == pbi.PbiId && x.ProjectId == pbi.ProjectId, cancellationToken);
            var currentGuidance = await new ProjectGuidelinesService(tenant).CurrentAsync(pbi.ProjectId, cancellationToken);
            if (current is null || current.Version != sourceVersion ||
                currentGuidance?.ProjectGuidelinesRevisionId != guidance?.ProjectGuidelinesRevisionId)
                throw new InvalidOperationException("The work item or project guidance changed during inference; reload and retry.");
        }
        return result; // Identity and immutable planning provenance remain server-loaded.
    }

    private void RequireInferenceSession()
    {
        if (tenant is not null && inference is null)
            throw new InvalidOperationException("Tenant inference requires a released-lease planning session.");
    }

    public Task<ProductBacklogItem> CreatePbiAsync(ProductBacklogItem story, CancellationToken cancellationToken = default)
    { story.IsDraft = false; return repository.AddAsync(story, cancellationToken); }
    public Task<ProductBacklogItem> CreateDraftPbiAsync(ProductBacklogItem story, CancellationToken cancellationToken = default)
    { story.IsDraft = true; return repository.AddAsync(story, cancellationToken); }
    public Task<List<ProductBacklogItem>> CreatePbisAsync(IEnumerable<ProductBacklogItem> stories, bool draft, CancellationToken cancellationToken = default)
    {
        var list = stories.ToList(); foreach (var story in list) story.IsDraft = draft;
        return repository.AddRangeAsync(list, cancellationToken);
    }
    public async Task<ProductBacklogItem> CommitDraftPbiAsync(ProductBacklogItem draftPbi, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetByIdAsync(draftPbi.PbiId, cancellationToken);
        if (existing == null || !existing.IsDraft) throw new KeyNotFoundException("Draft PBI not found.");
        return await CommitPbiAsync(existing, cancellationToken);
    }
    public Task<ProductBacklogItem> CommitPbiAsync(ProductBacklogItem pbi, CancellationToken cancellationToken = default)
    { pbi.IsDraft = false; return UpdatePbiAsync(pbi, cancellationToken); }
    public Task<ProductBacklogItem> UpdatePbiAsync(ProductBacklogItem pbi, CancellationToken cancellationToken = default)
    { pbi.LastUpdated = DateTime.UtcNow; return repository.UpdateAsync(pbi, cancellationToken); }
    public Task<bool> DeletePbiAsync(int id, CancellationToken cancellationToken = default) => repository.DeleteAsync(id, cancellationToken);
}
