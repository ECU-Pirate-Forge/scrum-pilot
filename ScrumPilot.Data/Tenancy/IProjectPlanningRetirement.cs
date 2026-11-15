namespace ScrumPilot.Data.Tenancy;

public interface IProjectPlanningRetirement
{
    Task RetireProjectAsync(int organizationId, int projectId, CancellationToken ct);
}
