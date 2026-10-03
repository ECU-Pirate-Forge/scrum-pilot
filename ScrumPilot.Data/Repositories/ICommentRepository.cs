using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Repositories
{
    /// <summary>
    /// Data-access contract for <see cref="Comment"/> persistence.
    /// </summary>
    public interface ICommentRepository
    {
        /// <summary>Returns all comments for the given <paramref name="pbiId"/>, ordered by most recent first.</summary>
        Task<IEnumerable<Comment>> GetByPbiIdAsync(int pbiId, CancellationToken cancellationToken = default);

        Task<Comment?> GetByIdAsync(int commentId, CancellationToken cancellationToken = default);

        /// <summary>Persists a new comment and returns it with its database-assigned ID.</summary>
        Task<Comment> AddAsync(Comment comment, CancellationToken cancellationToken = default);

        /// <summary>Saves changes to an existing comment and returns the updated entity.</summary>
        Task<Comment> UpdateAsync(Comment comment, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the comment with the given <paramref name="commentId"/>.
        /// Returns <c>true</c> if the record was found and removed; <c>false</c> otherwise.
        /// </summary>
        Task<bool> DeleteAsync(int commentId, CancellationToken cancellationToken = default);
    }
}