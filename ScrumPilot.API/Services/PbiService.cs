using Microsoft.EntityFrameworkCore;
using ScrumPilot.API.Services.AI;
using ScrumPilot.Data.Context;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Data.Tenancy;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services;

public sealed class PbiService(IStoryGenerationService generator, IPbiRepository repository,
    OrganizationContext? tenant = null, TenantStorage? storage = null, ControlPlaneContext? control = null,
    TenantSelection? selection = null) : IPbiService
{
    public const int MaxGenerationBatchSize = 10;
    public const int MaxProblemStatementLength = 10000;
    public Task<IEnumerable<ProductBacklogItem>> GetFilteredPbisAsync(int? sprintId, int? epicId, int? projectId = null, CancellationToken cancellationToken = default) =>
        repository.GetFilteredPbisAsync(sprintId, epicId, projectId, cancellationToken);

    private async Task<T> InferAsync<T>(Func<Task<T>> infer)
    {
        if (tenant?.HasStorageLease != true) return await infer();
        tenant.ChangeTracker.Clear();
        await tenant.Database.CloseConnectionAsync();
        tenant.ReleaseLease();
        try { return await infer(); }
        finally
        {
            var org = await control!.Organizations.AsNoTracking().SingleAsync(x => x.OrganizationId == selection!.OrganizationId);
            using var verification = storage!.Open(org);
            tenant.AdoptLease(verification);
            await tenant.Database.OpenConnectionAsync();
            TenantStorage.Verify(tenant, org);
        }
    }
    public Task<List<ProductBacklogItem>> GenerateAiPbis(List<string> problemStatements, CancellationToken cancellationToken = default) =>
        InferAsync(() => generator.GenerateAsync(problemStatements, cancellationToken));
    public Task<ProductBacklogItem> ImprovePbiAsync(ProductBacklogItem pbi, CancellationToken cancellationToken = default) =>
        InferAsync(() => generator.ImproveAsync(pbi, cancellationToken));
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
