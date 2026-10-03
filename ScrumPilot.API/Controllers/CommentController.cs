using Microsoft.AspNetCore.Mvc;
using ScrumPilot.API.Authorization;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers;

[ApiController]
[Route("api/comments")]
[Produces("application/json")]
public class CommentController(
    ICommentRepository commentRepository,
    IPbiRepository pbiRepository,
    ICurrentUser currentUser,
    IOrganizationAccessService accessService) : ControllerBase
{
    [HttpGet("pbi/{pbiId:int}")]
    public async Task<ActionResult<IEnumerable<Comment>>> GetCommentsByPbiId(
        int pbiId,
        CancellationToken cancellationToken = default)
    {
        if (await AuthorizedPbi(pbiId, cancellationToken) is null) return NotFound();
        return Ok(await commentRepository.GetByPbiIdAsync(pbiId, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<Comment>> AddComment(
        [FromBody] Comment request,
        CancellationToken cancellationToken = default)
    {
        if (await AuthorizedPbi(request.PbiId, cancellationToken) is null) return NotFound();
        var comment = new Comment
        {
            PbiId = request.PbiId,
            UserId = currentUser.UserId,
            Body = request.Body,
            CreatedDate = DateTime.UtcNow
        };
        var created = await commentRepository.AddAsync(comment, cancellationToken);
        return CreatedAtAction(nameof(GetCommentsByPbiId), new { pbiId = created.PbiId }, created);
    }

    [HttpPut("{commentId:int}")]
    public async Task<ActionResult<Comment>> EditComment(
        int commentId,
        [FromBody] Comment request,
        CancellationToken cancellationToken = default)
    {
        if (commentId != request.CommentId) return BadRequest();
        var existing = await commentRepository.GetByIdAsync(commentId, cancellationToken);
        if (existing is null
            || existing.UserId != currentUser.UserId
            || await AuthorizedPbi(existing.PbiId, cancellationToken) is null)
            return NotFound();

        existing.Body = request.Body;
        return Ok(await commentRepository.UpdateAsync(existing, cancellationToken));
    }

    [HttpDelete("{commentId:int}")]
    public async Task<IActionResult> DeleteComment(int commentId, CancellationToken cancellationToken = default)
    {
        var existing = await commentRepository.GetByIdAsync(commentId, cancellationToken);
        if (existing is null
            || existing.UserId != currentUser.UserId
            || await AuthorizedPbi(existing.PbiId, cancellationToken) is null)
            return NotFound();

        return await commentRepository.DeleteAsync(commentId, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    private async Task<ProductBacklogItem?> AuthorizedPbi(int pbiId, CancellationToken cancellationToken)
    {
        var pbi = await pbiRepository.GetByIdAsync(pbiId, cancellationToken);
        return pbi is not null
            && await accessService.CanAccessProjectAsync(
                currentUser.UserId, pbi.ProjectId, cancellationToken)
            ? pbi
            : null;
    }
}
