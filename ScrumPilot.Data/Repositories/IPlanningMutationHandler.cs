namespace ScrumPilot.Data.Repositories;

public interface IPlanningMutationHandler
{
    Task SaveAsync(CancellationToken cancellationToken);
}
