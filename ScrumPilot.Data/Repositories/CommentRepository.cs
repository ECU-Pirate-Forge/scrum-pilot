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

        public async Task<IEnumerable<Comment>> GetByPbiIdAsync(int pbiId)
        {
            return await _context.Comments
                .Where(c => c.PbiId == pbiId)
                .OrderByDescending(c => c.CreatedDate)
                .ToListAsync();
        }

        public async Task<Comment> AddAsync(Comment comment)
        {
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync();
            return comment;
        }

        public async Task<Comment> UpdateAsync(Comment comment)
        {
            _context.Entry(comment).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            return comment;
        }

        public async Task<bool> DeleteAsync(int commentId)
        {
            var comment = await _context.Comments.FindAsync(commentId);
            if (comment == null)
            {
                return false;
            }
            _context.Comments.Remove(comment);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
