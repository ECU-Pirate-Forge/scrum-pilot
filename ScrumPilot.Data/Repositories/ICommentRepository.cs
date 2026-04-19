namespace ScrumPilot.Data.Repositories
{
    public interface ICommentRepository
    {
        Task<IEnumerable<ScrumPilot.Shared.Models.Comment>> GetByPbiIdAsync(int pbiId);
        Task<ScrumPilot.Shared.Models.Comment> AddAsync(ScrumPilot.Shared.Models.Comment comment);
        Task<ScrumPilot.Shared.Models.Comment> UpdateAsync(ScrumPilot.Shared.Models.Comment comment);
        Task<bool> DeleteAsync(int commentId);
    }
}