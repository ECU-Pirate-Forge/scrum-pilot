using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services
{
    /// <summary>
    /// Business-logic contract for managing <see cref="ProductBacklogItem"/> entities
    /// and AI story generation.
    /// </summary>
    public interface IPbiService
    {
        /// <summary>Returns all PBIs regardless of draft status.</summary>
        Task<IEnumerable<ProductBacklogItem>> GetAllPbisAsync();

        /// <summary>Returns all non-draft PBIs.</summary>
        Task<IEnumerable<ProductBacklogItem>> GetNonDraftPbisAsync();

        /// <summary>Returns all draft PBIs awaiting review and commit.</summary>
        Task<IEnumerable<ProductBacklogItem>> GetDraftPbisAsync();

        /// <summary>
        /// Returns non-draft PBIs filtered by any combination of sprint, epic, and project.
        /// Pass <c>sprintId = -1</c> to retrieve unassigned PBIs.
        /// </summary>
        Task<IEnumerable<ProductBacklogItem>> GetFilteredPbisAsync(int? sprintId, int? epicId, int? projectId = null);

        /// <summary>Creates and persists a non-draft PBI, returning the saved entity.</summary>
        Task<ProductBacklogItem> CreatePbiAsync(ProductBacklogItem story);

        /// <summary>Creates and persists a PBI in draft state, returning the saved entity.</summary>
        Task<ProductBacklogItem> CreateDraftPbiAsync(ProductBacklogItem story);

        /// <summary>Promotes a draft PBI to the backlog by fetching it by ID and clearing its draft flag.</summary>
        Task<ProductBacklogItem> CommitDraftPbiAsync(ProductBacklogItem draftPbi);

        /// <summary>Clears the draft flag on the supplied PBI and saves the change.</summary>
        Task<ProductBacklogItem> CommitPbiAsync(ProductBacklogItem pbi);

        /// <summary>Updates an existing PBI and returns the saved entity.</summary>
        Task<ProductBacklogItem> UpdatePbiAsync(ProductBacklogItem pbi);

        /// <summary>
        /// Permanently deletes the PBI with the given <paramref name="id"/>.
        /// Returns <c>true</c> if deleted; <c>false</c> if not found.
        /// </summary>
        Task<bool> DeletePbiAsync(int id);

        /// <summary>
        /// Calls the configured AI provider (Groq or local Ollama) for each problem statement
        /// and returns the generated draft PBIs without persisting them.
        /// </summary>
        Task<List<ProductBacklogItem>> GenerateAiPbis(List<string> problemStatements);

        /// <summary>
        /// Rewrites and improves an existing PBI using the configured AI provider.
        /// Returns the improved PBI without persisting changes.
        /// </summary>
        Task<ProductBacklogItem> ImprovePbiAsync(ProductBacklogItem pbi);
    }
}