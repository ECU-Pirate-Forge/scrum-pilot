using Microsoft.AspNetCore.Mvc;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommentController : ControllerBase
    {
        private readonly ICommentRepository _commentRepository;

        public CommentController(ICommentRepository commentRepository)
        {
            _commentRepository = commentRepository;
        }

        // GET: api/Comment/pbi/5
        [HttpGet("pbi/{pbiId}")]
        public async Task<ActionResult<IEnumerable<Comment>>> GetCommentsByPbiId(int pbiId)
        {
            var comments = await _commentRepository.GetByPbiIdAsync(pbiId);
            return Ok(comments);
        }

        // DELETE: api/Comment/5
        [HttpDelete("{commentId}")]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var result = await _commentRepository.DeleteAsync(commentId);
            if (!result)
                return NotFound();
            return NoContent();
        }

        // PUT: api/Comment/5
        [HttpPut("{commentId}")]
        public async Task<ActionResult<Comment>> EditComment(int commentId, [FromBody] Comment comment)
        {
            if (commentId != comment.CommentId)
                return BadRequest();
            var updated = await _commentRepository.UpdateAsync(comment);
            return Ok(updated);
        }
    }
}
