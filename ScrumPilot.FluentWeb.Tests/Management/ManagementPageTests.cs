using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.FluentWeb.Components.Organizations;
using ScrumPilot.FluentWeb.Pages;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.FluentWeb.Tests.TestHelpers;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Management;

public class ManagementPageTests : FluentFrontendTestBase
{
    private readonly ProjectStateService _projectState;
    private readonly OrganizationStateService _organizationState;
    private bool _nextConfirmation;
    private bool _lastDestructive;

    public ManagementPageTests()
    {
        _projectState = Services.GetRequiredService<ProjectStateService>();
        _organizationState = Services.GetRequiredService<OrganizationStateService>();
    }

    [Fact]
    public void Project_management_loads_and_renders_project_list()
    {
        SelectOrganization();
        RegisterProjectManagementRoutes([Project(10, "Apollo", "Migration work")]);

        using var cut = Render<ProjectManagement>();

        cut.WaitForAssertion(() => Assert.Contains("Apollo", cut.Markup));
        Assert.Contains("Migration work", cut.Markup);
        Assert.DoesNotContain("mud-", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Creating_project_posts_expected_payload_and_notifies_project_list_changed()
    {
        SelectOrganization();
        RegisterProjectManagementRoutes([]);
        Http.On(HttpMethod.Post, "api/organizations/7/projects", (_, body) =>
            MockHttpHandler.Json(Project(44, "Roadmap", "Quarterly plan"), HttpStatusCode.OK));
        var notified = false;
        _projectState.OnProjectListChanged += () => notified = true;

        using var cut = Render<ProjectManagement>();
        cut.WaitForAssertion(() => Assert.Contains("No projects yet", cut.Markup));

        cut.FindAll("fluent-button").First(button => button.TextContent.Contains("Add", StringComparison.OrdinalIgnoreCase)).Click();
        var inputs = cut.FindAll("fluent-text-input");
        inputs[0].Change("Roadmap");
        cut.Find("fluent-textarea").Change("Quarterly plan");
        cut.FindAll("fluent-button").First(button => button.TextContent.Contains("Save", StringComparison.OrdinalIgnoreCase)).Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/organizations/7/projects"));
            using var document = JsonDocument.Parse(request.Body!);
            Assert.Equal("Roadmap", document.RootElement.GetProperty("projectName").GetString());
            Assert.Equal("Quarterly plan", document.RootElement.GetProperty("description").GetString());
            Assert.True(notified);
        });
    }

    [Fact]
    public void Organization_members_render_loaded_members()
    {
        Http.OnGetJson("api/organizations/7/members", new List<OrganizationMemberDto>
        {
            new(7, "owner-user", OrganizationRole.Owner, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
            new(7, "member-user", OrganizationRole.Member, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)),
        });
        Http.OnGetJson("api/organizations/7/invitations", new List<OrganizationInvitationDto>());

        using var cut = Render<OrganizationMembers>(parameters => parameters
            .Add(p => p.OrganizationId, 7)
            .Add(p => p.CanManage, true));

        cut.WaitForAssertion(() => Assert.Contains("member-user", cut.Markup));
        Assert.Contains("owner-user", cut.Markup);
        Assert.Contains("Promote", cut.Markup);
    }

    [Fact]
    public void Delete_organization_requires_confirmation_before_request()
    {
        var organization = new OrganizationSummaryDto(7, "Acme", OrganizationRole.Owner, false);
        Http.OnStatus(HttpMethod.Delete, "api/organizations/7", HttpStatusCode.NoContent);

        _nextConfirmation = false;
        using var declined = Render<OrganizationDangerZone>(parameters => parameters
            .Add(p => p.Organization, organization)
            .Add(p => p.ConfirmAsync, ConfirmAsync));
        declined.Find("fluent-button[data-action='delete']").Click();
        Assert.Empty(Http.RequestsTo(HttpMethod.Delete, "api/organizations/7"));
        Assert.True(_lastDestructive);

        _nextConfirmation = true;
        using var accepted = Render<OrganizationDangerZone>(parameters => parameters
            .Add(p => p.Organization, organization)
            .Add(p => p.ConfirmAsync, ConfirmAsync));
        accepted.Find("fluent-button[data-action='delete']").Click();
        accepted.WaitForAssertion(() => Assert.Single(Http.RequestsTo(HttpMethod.Delete, "api/organizations/7")));
    }

    private void SelectOrganization() =>
        _organizationState.SetOrganization(new OrganizationSummaryDto(7, "Acme", OrganizationRole.Owner, false));

    private void RegisterProjectManagementRoutes(List<Project> projects)
    {
        Http.OnGetJson("api/organizations/7/projects", projects);
        Http.OnGetJson("api/sprint", new List<Sprint>());
        Http.OnGetJson("api/epic", new List<Epic>());
        Http.OnGetJson("api/pbi/getnondraftpbis", new List<ProductBacklogItem>());
    }

    private static Project Project(int id, string name, string? description = null) => new()
    {
        ProjectId = id,
        OrganizationId = 7,
        ProjectName = name,
        Description = description,
    };

    private Task<bool> ConfirmAsync(string title, string message, string confirmLabel, string cancelLabel, bool destructive)
    {
        _lastDestructive = destructive;
        return Task.FromResult(_nextConfirmation);
    }
}

