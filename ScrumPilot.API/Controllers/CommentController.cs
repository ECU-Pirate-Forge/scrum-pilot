using Microsoft.AspNetCore.Mvc;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    /// <summary>
    /// Manages comments associated with Product Backlog Items.
    /// </summary>
    [ApiController]
    [Route("api/comments")]
    [Produces("application/json")]
    public class CommentController : ControllerBase
    {
        private readonly ICommentRepository _commentRepository;

        /// <summary>
        /// Initializes a new instance of <see cref="CommentController"/>.
        /// </summary>
        /// <param name="commentRepository">The comment repository.</param>
        public CommentController(ICommentRepository commentRepository)
        {
            _commentRepository = commentRepository;
        }

        /// <summary>
        /// Retrieves all comments for a given PBI.
        /// </summary>
        /// <param name="pbiId">The ID of the Product Backlog Item.</param>
        /// <returns>A list of comments ordered by most recent first.</returns>
        /// <response code="200">Returns the list of comments.</response>
        [HttpGet("pbi/{pbiId:int}")]
        [ProducesResponseType(typeof(IEnumerable<Comment>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<Comment>>> GetCommentsByPbiId(int pbiId)
        {
            var comments = await _commentRepository.GetByPbiIdAsync(pbiId);
            return Ok(comments);
        }

        /// <summary>
        /// Creates a new comment on a PBI.
        /// </summary>
        /// <param name="comment">The comment to create.</param>
        /// <returns>The newly created comment.</returns>
        /// <response code="201">Returns the created comment.</response>
        /// <response code="400">If the request body is invalid.</response>
        [HttpPost]
        [ProducesResponseType(typeof(Comment), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<Comment>> AddComment([FromBody] Comment comment)
        {
            comment.CreatedDate = DateTime.UtcNow;
            var created = await _commentRepository.AddAsync(comment);
            return CreatedAtAction(nameof(GetCommentsByPbiId), new { pbiId = created.PbiId }, created);
        }

        /// <summary>
        /// Updates the body of an existing comment.
        /// </summary>
        /// <param name="commentId">The ID of the comment to update.</param>
        /// <param name="comment">The updated comment data.</param>
        /// <returns>The updated comment.</returns>
        /// <response code="200">Returns the updated comment.</response>
        /// <response code="400">If the route ID does not match the body ID.</response>
        /// <response code="404">If no comment with the given ID exists.</response>
        [HttpPut("{commentId:int}")]
        [ProducesResponseType(typeof(Comment), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Comment>> EditComment(int commentId, [FromBody] Comment comment)
        {
            if (commentId != comment.CommentId)
                return BadRequest();

            var existing = await _commentRepository.GetByPbiIdAsync(comment.PbiId);
            if (!existing.Any(c => c.CommentId == commentId))
                return NotFound();

            var updated = await _commentRepository.UpdateAsync(comment);
            return Ok(updated);
        }

        /// <summary>
        /// Deletes a comment by ID.
        /// </summary>
        /// <param name="commentId">The ID of the comment to delete.</param>
        /// <returns>No content on success.</returns>
        /// <response code="204">The comment was deleted successfully.</response>
        /// <response code="404">If no comment with the given ID exists.</response>
        [HttpDelete("{commentId:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var result = await _commentRepository.DeleteAsync(commentId);
            if (!result)
                return NotFound();
            return NoContent();
        }
    }
}
