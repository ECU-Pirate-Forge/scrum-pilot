using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Services;

namespace ScrumPilot.UnitTests.Frontend.ServiceTests;

public class OrganizationStateServiceTests
{
    [Fact]
    public void SetOrganization_UpdatesSelectionAndRaisesChange()
    {
        var service = new OrganizationStateService();
        var organization = new OrganizationSummaryDto(7, "Accessible", OrganizationRole.Member, false);
        var changes = 0;
        service.OnChange += () => changes++;

        service.SetOrganization(organization);

        Assert.Same(organization, service.SelectedOrganization);
        Assert.Equal(7, service.SelectedOrganizationId);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Clear_RemovesOrganizationAndRaisesChange()
    {
        var service = new OrganizationStateService();
        service.SetOrganization(
            new OrganizationSummaryDto(7, "Accessible", OrganizationRole.Member, false));
        var changes = 0;
        service.OnChange += () => changes++;

        service.Clear();

        Assert.Null(service.SelectedOrganization);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ProjectClear_RemovesProjectAndRaisesSelectionEvent()
    {
        var service = new ProjectStateService();
        service.SetProject(new Project { ProjectId = 3, OrganizationId = 7, ProjectName = "Project" });
        var selectionChanges = 0;
        service.OnChange += () => selectionChanges++;

        service.Clear();

        Assert.Null(service.SelectedProject);
        Assert.Equal(1, selectionChanges);
    }

    [Fact]
    public void NotifyProjectListChanged_RaisesListEvent()
    {
        var service = new ProjectStateService();
        var listChanges = 0;
        service.OnProjectListChanged += () => listChanges++;

        service.NotifyProjectListChanged();

        Assert.Equal(1, listChanges);
    }
}
