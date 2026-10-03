using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public class CommentRepository : ICommentRepository
    {
        private readonly ScrumPilotContext _context;

        public CommentRepository(ScrumPilotContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Comment>> GetByPbiIdAsync(int pbiId, CancellationToken cancellationToken = default)
        {
            return await _context.Comments
                .Where(c => c.PbiId == pbiId)
                .OrderByDescending(c => c.CreatedDate)
                .ToListAsync(cancellationToken);
        }

        public Task<Comment?> GetByIdAsync(int commentId, CancellationToken cancellationToken = default) =>
            _context.Comments.FirstOrDefaultAsync(c => c.CommentId == commentId, cancellationToken);

        public async Task<Comment> AddAsync(Comment comment, CancellationToken cancellationToken = default)
        {
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync(cancellationToken);
            return comment;
        }

        public async Task<Comment> UpdateAsync(Comment comment, CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return comment;
        }

        public async Task<bool> DeleteAsync(int commentId, CancellationToken cancellationToken = default)
        {
            var comment = await _context.Comments.FindAsync([commentId], cancellationToken);
            if (comment == null)
            {
                return false;
            }
            _context.Comments.Remove(comment);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
