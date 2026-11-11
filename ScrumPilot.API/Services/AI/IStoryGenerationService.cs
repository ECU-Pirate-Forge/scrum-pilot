using ScrumPilot.Shared.Models;

namespace ScrumPilot.API.Services.AI;

public interface IStoryGenerationService
{
    Task<List<ProductBacklogItem>> GenerateAsync(IReadOnlyList<string> statements, CancellationToken ct = default);
    Task<ProductBacklogItem> ImproveAsync(ProductBacklogItem story, CancellationToken ct = default);
}
