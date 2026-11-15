using Microsoft.EntityFrameworkCore;
using ScrumPilot.Data.Context;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    public class CommentRepository : ICommentRepository
    {
        private readonly ScrumPilotContext _context;
        public ControlPlaneContext? Directory { get; init; }
        private async Task HydrateUsers(IEnumerable<Comment> comments, CancellationToken ct)
        {
            if (Directory is null) return;
            var list = comments.ToList();
            var ids = list.Select(x => x.UserId).Distinct().ToArray();
            var names = await Directory.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UserName, ct);
            foreach (var comment in list) comment.AuthorUserName = names.GetValueOrDefault(comment.UserId) ?? "Former member";
        }

        public CommentRepository(ScrumPilotContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Comment>> GetByPbiIdAsync(int pbiId, CancellationToken cancellationToken = default)
        {
            var comments = await _context.Comments
                .Where(c => c.PbiId == pbiId)
                .OrderByDescending(c => c.CreatedDate)
                .ToListAsync(cancellationToken);
            await HydrateUsers(comments, cancellationToken);
            return comments;
        }

        public async Task<Comment?> GetByIdAsync(int commentId, CancellationToken cancellationToken = default)
        {
            var comment = await _context.Comments.FirstOrDefaultAsync(c => c.CommentId == commentId, cancellationToken);
            if (comment != null) await HydrateUsers([comment], cancellationToken);
            return comment;
        }

        public async Task<Comment> AddAsync(Comment comment, CancellationToken cancellationToken = default)
        {
            _context.Comments.Add(comment);
            await _context.SaveChangesAsync(cancellationToken);
            await HydrateUsers([comment], cancellationToken);
            return comment;
        }

        public async Task<Comment> UpdateAsync(Comment comment, CancellationToken cancellationToken = default)
        {
            await _context.SaveChangesAsync(cancellationToken);
            await HydrateUsers([comment], cancellationToken);
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
