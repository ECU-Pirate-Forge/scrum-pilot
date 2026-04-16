using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    public interface IPbiService
    {
        Task<ProductBacklogItem> CommitDraftPbiAsync(ProductBacklogItem draftPbi);
        Task<ProductBacklogItem> CommitPbiAsync(ProductBacklogItem pbi);
        Task<ProductBacklogItem> CreateDraftPbiAsync(ProductBacklogItem story);
        Task<ProductBacklogItem> CreatePbiAsync(ProductBacklogItem story);
        Task<bool> DeletePbiAsync(int id);
        Task<List<ProductBacklogItem>> GenerateAiPbis(List<string> problemStatements);
        Task<ProductBacklogItem> ImprovePbiAsync(ProductBacklogItem pbi);
        Task<IEnumerable<ProductBacklogItem>> GetAllPbisAsync();
        Task<IEnumerable<ProductBacklogItem>> GetDraftPbisAsync();
        Task<IEnumerable<ProductBacklogItem>> GetNonDraftPbisAsync();
        Task<ProductBacklogItem> UpdatePbiAsync(ProductBacklogItem pbi);
    }
}