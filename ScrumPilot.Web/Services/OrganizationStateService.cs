using ScrumPilot.Shared.Models;

namespace ScrumPilot.Web.Services;

public class OrganizationStateService
{
    public OrganizationSummaryDto? SelectedOrganization { get; private set; }

    public int? SelectedOrganizationId => SelectedOrganization?.OrganizationId;

    public event Action? OnChange;

    public void SetOrganization(OrganizationSummaryDto? organization)
    {
        SelectedOrganization = organization;
        OnChange?.Invoke();
    }

    public void Clear() => SetOrganization(null);
}
