using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PbiController(
    IPbiService pbiService,
    IPbiRepository pbiRepository,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService) : ControllerBase
{
    public const int MaxBulkCreateBatchSize = 100;

    [HttpGet("getAllPbis")]
    public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetAllPbis(
        [FromQuery] int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();
        return Ok(await pbiRepository.GetByProjectAsync(projectId, null, cancellationToken));
    }

    [HttpGet("getNonDraftPbis")]
    public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetNonDraftPbis(
        [FromQuery] int projectId,
        [FromQuery] int? sprintId = null,
        [FromQuery] int? epicId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();
        var sprintIdToValidate = sprintId == -1 ? null : sprintId;
        if (!await RelatedIdsBelongToProject(
                projectId, sprintIdToValidate, epicId, null, null, cancellationToken))
            return NotFound();
        return Ok(await pbiService.GetFilteredPbisAsync(sprintId, epicId, projectId, cancellationToken));
    }

    [HttpGet("getDraftPbis")]
    public async Task<ActionResult<IEnumerable<ProductBacklogItem>>> GetDraftPbis(
        [FromQuery] int projectId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();
        return Ok(await pbiRepository.GetByProjectAsync(projectId, true, cancellationToken));
    }

    [HttpPost("generateAiPbis")]
    public async Task<ActionResult<List<ProductBacklogItem>>> GenerateAiPbis(
        [FromBody] List<string> problemStatements)
    {
        if (problemStatements is null || problemStatements.Count == 0)
            return BadRequest("At least one problem statement is required.");
        if (problemStatements.Any(string.IsNullOrWhiteSpace))
            return BadRequest("All problem statements must be non-empty strings.");

        try
        {
            return Ok(await pbiService.GenerateAiPbis(problemStatements));
        }
        catch (InvalidOperationException ex) { return BadRequest($"Failed to generate AI PBIs: {ex.Message}"); }
        catch (HttpRequestException ex) { return StatusCode(502, $"Failed to communicate with AI service: {ex.Message}"); }
        catch (TimeoutException ex) { return StatusCode(408, $"Request timed out: {ex.Message}"); }
        catch (Exception ex) { return StatusCode(500, $"An unexpected error occurred: {ex.Message}"); }
    }

    [HttpPost("ImprovePbi")]
    public async Task<ActionResult<ProductBacklogItem>> ImprovePbi(
        [FromBody] ProductBacklogItem request,
        CancellationToken cancellationToken = default)
    {
        var existing = await AuthorizedPbi(request.PbiId, cancellationToken);
        if (existing is null) return NotFound();
        request.SprintId = NormalizeSprintId(request.SprintId);
        if (!await ChangedRelatedIdsBelongToProject(existing, request, cancellationToken))
            return NotFound();
        ApplyMutableFields(existing, request);
        return Ok(await pbiService.ImprovePbiAsync(existing));
    }

    [HttpPost("createStory")]
    public Task<ActionResult<ProductBacklogItem>> CreatePbi(
        [FromQuery] int projectId,
        [FromBody] ProductBacklogItem request,
        CancellationToken cancellationToken = default) =>
        Create(request, projectId, false, PbiOrigin.WebUserCreated, cancellationToken);

    [HttpPost("createDraftPbi")]
    public Task<ActionResult<ProductBacklogItem>> CreateDraftPbi(
        [FromQuery] int projectId,
        [FromBody] ProductBacklogItem request,
        CancellationToken cancellationToken = default) =>
        Create(request, projectId, true, PbiOrigin.WebUserCreated, cancellationToken);

    [HttpPost("createStories")]
    public Task<ActionResult<List<ProductBacklogItem>>> CreatePbis(
        [FromQuery] int projectId,
        [FromBody] List<ProductBacklogItem> requests,
        CancellationToken cancellationToken = default) =>
        CreateMany(requests, projectId, false, cancellationToken);

    [HttpPost("createDraftPbis")]
    public Task<ActionResult<List<ProductBacklogItem>>> CreateDraftPbis(
        [FromQuery] int projectId,
        [FromBody] List<ProductBacklogItem> requests,
        CancellationToken cancellationToken = default) =>
        CreateMany(requests, projectId, true, cancellationToken);

    [HttpPost("commitPbi")]
    public async Task<ActionResult<ProductBacklogItem>> CommitPbi(
        [FromBody] ProductBacklogItem request,
        CancellationToken cancellationToken = default)
    {
        var existing = await AuthorizedPbi(request.PbiId, cancellationToken);
        if (existing is null) return NotFound();
        existing.IsDraft = false;
        return Ok(await pbiService.CommitPbiAsync(existing, cancellationToken));
    }

    [HttpPut]
    public async Task<ActionResult<ProductBacklogItem>> UpdatePbi(
        [FromBody] ProductBacklogItem request,
        CancellationToken cancellationToken = default)
    {
        var existing = await AuthorizedPbi(request.PbiId, cancellationToken);
        if (existing is null) return NotFound();
        request.SprintId = NormalizeSprintId(request.SprintId);
        if (!await ChangedRelatedIdsBelongToProject(existing, request, cancellationToken))
            return NotFound();

        ApplyMutableFields(existing, request);
        return Ok(await pbiService.UpdatePbiAsync(existing, cancellationToken));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeletePbi(int id, CancellationToken cancellationToken = default)
    {
        if (await AuthorizedPbi(id, cancellationToken) is null) return NotFound();
        return await pbiService.DeletePbiAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private async Task<ActionResult<ProductBacklogItem>> Create(
        ProductBacklogItem request,
        int projectId,
        bool draft,
        PbiOrigin origin,
        CancellationToken cancellationToken)
    {
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();
        request.SprintId = NormalizeSprintId(request.SprintId);
        if (!await RelatedIdsBelongToProject(
                projectId, request.SprintId, request.EpicId,
                request.AssignedToUserId, request.DependsOnPbiId, cancellationToken))
            return NotFound();

        var item = NewPbi(request, projectId, origin);
        var created = draft
            ? await pbiService.CreateDraftPbiAsync(item, cancellationToken)
            : await pbiService.CreatePbiAsync(item, cancellationToken);
        return Ok(created);
    }

    private async Task<ActionResult<List<ProductBacklogItem>>> CreateMany(
        List<ProductBacklogItem> requests,
        int projectId,
        bool draft,
        CancellationToken cancellationToken)
    {
        if (requests.Count > MaxBulkCreateBatchSize)
            return BadRequest($"A maximum of {MaxBulkCreateBatchSize} PBIs can be created at once.");
        if (!await CanAccess(projectId, cancellationToken)) return NotFound();

        foreach (var request in requests)
            request.SprintId = NormalizeSprintId(request.SprintId);

        if (!await DistinctRelatedIdsBelongToProject(requests, projectId, cancellationToken))
            return NotFound();

        var items = requests.Select(request => NewPbi(request, projectId, PbiOrigin.AiGenerated));
        return Ok(await pbiService.CreatePbisAsync(items, draft, cancellationToken));
    }

    private async Task<ProductBacklogItem?> AuthorizedPbi(int id, CancellationToken cancellationToken)
    {
        var pbi = await pbiRepository.GetByIdAsync(id, cancellationToken);
        return pbi is not null && await CanAccess(pbi.ProjectId, cancellationToken) ? pbi : null;
    }

    private Task<bool> CanAccess(int projectId, CancellationToken cancellationToken) =>
        accessService.CanAccessProjectAsync(currentUser.UserId, projectId, cancellationToken);

    private async Task<bool> RelatedIdsBelongToProject(
        int projectId,
        int? sprintId,
        int? epicId,
        string? assignedUserId,
        int? dependsOnPbiId,
        CancellationToken cancellationToken)
    {
        if (sprintId.HasValue
            && !await accessService.SprintBelongsToProjectAsync(sprintId.Value, projectId, cancellationToken))
            return false;
        if (epicId.HasValue
            && !await accessService.EpicBelongsToProjectAsync(epicId.Value, projectId, cancellationToken))
            return false;
        if (assignedUserId is not null
            && !await accessService.UserCanBeAssignedToProjectAsync(assignedUserId, projectId, cancellationToken))
            return false;
        return !dependsOnPbiId.HasValue
            || await accessService.PbiBelongsToProjectAsync(dependsOnPbiId.Value, projectId, cancellationToken);
    }

    private async Task<bool> ChangedRelatedIdsBelongToProject(
        ProductBacklogItem existing,
        ProductBacklogItem request,
        CancellationToken cancellationToken)
    {
        var projectId = existing.ProjectId;
        if (request.SprintId != existing.SprintId && request.SprintId.HasValue
            && !await accessService.SprintBelongsToProjectAsync(
                request.SprintId.Value, projectId, cancellationToken))
            return false;
        if (request.EpicId != existing.EpicId && request.EpicId.HasValue
            && !await accessService.EpicBelongsToProjectAsync(
                request.EpicId.Value, projectId, cancellationToken))
            return false;
        if (request.AssignedToUserId != existing.AssignedToUserId
            && request.AssignedToUserId is not null
            && !await accessService.UserCanBeAssignedToProjectAsync(
                request.AssignedToUserId, projectId, cancellationToken))
            return false;
        return request.DependsOnPbiId == existing.DependsOnPbiId
            || !request.DependsOnPbiId.HasValue
            || await accessService.PbiBelongsToProjectAsync(
                request.DependsOnPbiId.Value, projectId, cancellationToken);
    }

    private async Task<bool> DistinctRelatedIdsBelongToProject(
        IEnumerable<ProductBacklogItem> requests,
        int projectId,
        CancellationToken cancellationToken)
    {
        var items = requests.ToList();
        foreach (var sprintId in items.Where(x => x.SprintId.HasValue).Select(x => x.SprintId!.Value).Distinct())
            if (!await accessService.SprintBelongsToProjectAsync(sprintId, projectId, cancellationToken))
                return false;
        foreach (var epicId in items.Where(x => x.EpicId.HasValue).Select(x => x.EpicId!.Value).Distinct())
            if (!await accessService.EpicBelongsToProjectAsync(epicId, projectId, cancellationToken))
                return false;
        foreach (var userId in items.Select(x => x.AssignedToUserId)
                     .Where(x => x is not null).Cast<string>().Distinct(StringComparer.Ordinal))
            if (!await accessService.UserCanBeAssignedToProjectAsync(userId, projectId, cancellationToken))
                return false;
        foreach (var pbiId in items.Where(x => x.DependsOnPbiId.HasValue)
                     .Select(x => x.DependsOnPbiId!.Value).Distinct())
            if (!await accessService.PbiBelongsToProjectAsync(pbiId, projectId, cancellationToken))
                return false;
        return true;
    }

    private static int? NormalizeSprintId(int? sprintId) => sprintId == -1 ? null : sprintId;

    private static ProductBacklogItem NewPbi(
        ProductBacklogItem request,
        int projectId,
        PbiOrigin origin)
    {
        var item = new ProductBacklogItem { ProjectId = projectId, Title = request.Title, Origin = origin };
        ApplyMutableFields(item, request);
        item.ProjectId = projectId;
        item.PbiId = 0;
        item.DateCreated = DateTime.UtcNow;
        item.LastUpdated = item.DateCreated;
        return item;
    }

    private static void ApplyMutableFields(ProductBacklogItem target, ProductBacklogItem source)
    {
        target.Type = source.Type;
        target.EpicId = source.EpicId;
        target.SprintId = source.SprintId;
        target.Title = source.Title;
        target.Description = source.Description;
        target.Status = source.Status;
        target.Priority = source.Priority;
        target.StoryPoints = source.StoryPoints;
        target.IsFlagged = source.IsFlagged;
        target.AssignedToUserId = source.AssignedToUserId;
        target.IssueLink = source.IssueLink;
        target.DependsOnPbiId = source.DependsOnPbiId;
    }
}
