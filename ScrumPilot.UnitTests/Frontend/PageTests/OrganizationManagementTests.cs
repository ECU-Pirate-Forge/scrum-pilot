using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components.Organizations;
using ScrumPilot.Web.Pages;
using ScrumPilot.Web.Services;

namespace ScrumPilot.UnitTests.Frontend.PageTests;

public sealed class OrganizationManagementTests : FrontendTestBase
{
    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    [Fact]
    public void Owner_sees_management_controls_while_member_does_not()
    {
        var owner = new OrganizationSummaryDto(7, "Forge", OrganizationRole.Owner, false);
        var ownerView = Render<OrganizationDetails>(p => p.Add(x => x.Organization, owner));
        Assert.Contains("Rename organization", ownerView.Markup);

        var member = owner with { Role = OrganizationRole.Member };
        var memberView = Render<OrganizationDetails>(p => p.Add(x => x.Organization, member));
        Assert.DoesNotContain("Rename organization", memberView.Markup);
    }

    [Fact]
    public async Task Rename_submits_organization_route()
    {
        HttpResponseFactory = _ => Json(new OrganizationSummaryDto(7, "New Forge", OrganizationRole.Owner, false));
        var cut = Render<OrganizationDetails>(p => p.Add(x => x.Organization,
            new OrganizationSummaryDto(7, "Forge", OrganizationRole.Owner, false)));

        cut.Find("input").Change("New Forge");
        await cut.Find("button[data-action='rename']").ClickAsync(new());

        Assert.Contains(HttpRequestLog, x => x.Method == HttpMethod.Put && x.Url == "api/organizations/7");
    }

    [Fact]
    public async Task Invitation_and_member_changes_use_expected_routes_and_surface_conflict()
    {
        HttpResponseFactory = request => request.RequestUri!.AbsolutePath.EndsWith("/members")
            ? Json(new[] { new OrganizationMemberDto(7, "user-1", OrganizationRole.Member, DateTime.UtcNow) })
            : request.Method == HttpMethod.Post
                ? Json(new OrganizationInvitationDto(3, 7, "new@example.com", "owner", OrganizationRole.Member,
                    OrganizationInvitationStatus.Pending, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null, null, null),
                    HttpStatusCode.Created)
                : new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("""{"detail":"The organization must retain an owner."}""", Encoding.UTF8, "application/problem+json")
                };
        var cut = Render<OrganizationMembers>(p => p
            .Add(x => x.OrganizationId, 7)
            .Add(x => x.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("user-1"));

        cut.Find("input[type='email']").Change("new@example.com");
        await cut.Find("button[data-action='invite']").ClickAsync(new());
        cut.Find("input[type='checkbox']").Change(true);
        await cut.Find("button[data-action='promote']").ClickAsync(new());

        Assert.Contains(HttpRequestLog, x => x.Method == HttpMethod.Post && x.Url == "api/organizations/7/invitations");
        Assert.Contains(HttpRequestLog, x => x.Method == HttpMethod.Put && x.Url == "api/organizations/7/members/user-1/role");
        Assert.Contains("must retain an owner", cut.Markup);
    }

    [Fact]
    public async Task Project_access_toggle_refreshes_access()
    {
        var calls = 0;
        HttpResponseFactory = request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/members"))
            {
                calls++;
                return Json(new[] { new ProjectMemberAccessDto("member", OrganizationRole.Member, false) });
            }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };
        var cut = Render<ProjectAccessEditor>(p => p.Add(x => x.ProjectId, 12).Add(x => x.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("member"));
        await cut.Find("button[data-user='member']").ClickAsync(new());

        Assert.Contains(HttpRequestLog, x => x.Method == HttpMethod.Put && x.Url == "api/projects/12/members/member");
        Assert.True(calls >= 2);
    }

    [Fact]
    public async Task Danger_zone_supports_leave_and_owner_delete()
    {
        HttpResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.NoContent);
        var cut = Render<OrganizationDangerZone>(p => p
            .Add(x => x.Organization, new OrganizationSummaryDto(7, "Forge", OrganizationRole.Owner, false)));
        foreach (var checkbox in cut.FindAll("input[type='checkbox']"))
            checkbox.Change(true);
        await cut.Find("button[data-action='leave']").ClickAsync(new());
        cut.FindAll("input[type='checkbox']")[1].Change(true);
        await cut.Find("button[data-action='delete']").ClickAsync(new());

        Assert.Contains(HttpRequestLog, x => x.Method == HttpMethod.Post && x.Url == "api/organizations/7/leave");
        Assert.Contains(HttpRequestLog, x => x.Method == HttpMethod.Delete && x.Url == "api/organizations/7");
    }

    [Fact]
    public async Task Danger_zone_resets_confirmation_and_ignores_completion_after_organization_changes()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseFactoryAsync = (_, _) => delayed.Task;
        var changed = 0;
        var cut = Render<OrganizationDangerZone>(parameters => parameters
            .Add(component => component.Organization,
                new OrganizationSummaryDto(1, "A", OrganizationRole.Owner, false))
            .Add(component => component.Changed, () => changed++));
        cut.FindAll("input[type='checkbox']")[0].Change(true);
        var leave = cut.Find("button[data-action='leave']");
        var mutation = leave.ClickAsync(new());

        cut.Render(parameters => parameters
            .Add(component => component.Organization,
                new OrganizationSummaryDto(2, "B", OrganizationRole.Owner, false))
            .Add(component => component.Changed, () => changed++));

        Assert.True(cut.Find("button[data-action='leave']").HasAttribute("disabled"));
        Assert.True(cut.Find("button[data-action='delete']").HasAttribute("disabled"));

        delayed.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await mutation;

        Assert.Equal(0, changed);
        Assert.DoesNotContain("Unable", cut.Markup);
    }

    [Fact]
    public void Global_admin_sees_create_organization_form()
    {
        Authorization.SetAuthorized("admin");
        Authorization.SetRoles("Admin");
        var state = Services.GetRequiredService<OrganizationStateService>();
        state.SetOrganization(new OrganizationSummaryDto(7, "Forge", OrganizationRole.Member, false));
        HttpResponseFactory = request => request.RequestUri!.AbsolutePath.EndsWith("/organizations")
            ? Json(new[] { state.SelectedOrganization! })
            : Json(Array.Empty<Project>());

        var cut = Render<OrganizationManagement>();
        cut.WaitForState(() => cut.Markup.Contains("Create organization"));
        Assert.Contains("Initial owner user ID", cut.Markup);
    }

    [Fact]
    public void Deleted_organizations_are_reachable_without_loading_their_projects()
    {
        var deleted = new OrganizationSummaryDto(9, "Deleted Forge", OrganizationRole.Owner, true);
        HttpResponseFactory = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/organizations/deleted", StringComparison.Ordinal))
                return Json(new[] { deleted });
            if (path.EndsWith("/organizations", StringComparison.Ordinal))
                return Json(Array.Empty<OrganizationSummaryDto>());
            return Json(Array.Empty<Project>());
        };

        var cut = Render<OrganizationManagement>();
        cut.WaitForState(() => cut.Markup.Contains("Deleted Forge"));

        Assert.Contains("Deleted organizations", cut.Markup);
        Assert.Contains("data-action=\"restore\"", cut.Markup);
        Assert.DoesNotContain("api/organizations/9/projects", HttpRequests);
    }

    [Fact]
    public void Reload_replaces_stale_selection_and_clears_project_state()
    {
        var organizationState = Services.GetRequiredService<OrganizationStateService>();
        var projectState = Services.GetRequiredService<ProjectStateService>();
        organizationState.SetOrganization(
            new OrganizationSummaryDto(1, "A", OrganizationRole.Owner, false));
        projectState.SetProject(new Project
        {
            ProjectId = 11,
            OrganizationId = 1,
            ProjectName = "A project"
        });
        var fallback = new OrganizationSummaryDto(2, "B", OrganizationRole.Owner, false);
        HttpResponseFactory = request =>
            request.RequestUri!.AbsolutePath.EndsWith("/organizations/deleted")
                ? Json(Array.Empty<OrganizationSummaryDto>())
                : request.RequestUri.AbsolutePath.EndsWith("/organizations")
                    ? Json(new[] { fallback })
                    : Json(Array.Empty<Project>());

        var cut = Render<OrganizationManagement>();
        cut.WaitForState(() => cut.Markup.Contains("B"));

        Assert.Equal(2, organizationState.SelectedOrganizationId);
        Assert.Null(projectState.SelectedProjectId);
        Assert.DoesNotContain("A project", cut.Markup);
    }

    [Fact]
    public void Project_management_uses_selected_organization_and_hides_owner_controls_from_member()
    {
        Services.GetRequiredService<OrganizationStateService>().SetOrganization(
            new OrganizationSummaryDto(7, "Forge", OrganizationRole.Member, false));
        HttpResponseFactory = _ => Json(Array.Empty<Project>());

        var cut = Render<ProjectManagement>();
        cut.WaitForState(() => HttpRequests.Count > 0);

        Assert.Contains("api/organizations/7/projects", HttpRequests);
        Assert.DoesNotContain("Add Project", cut.Markup);
    }

    [Fact]
    public void Project_management_loads_when_organization_is_selected_after_initial_render()
    {
        HttpResponseFactory = _ => Json(Array.Empty<Project>());
        var state = Services.GetRequiredService<OrganizationStateService>();
        var cut = Render<ProjectManagement>();

        Assert.Empty(HttpRequests);
        cut.InvokeAsync(() => state.SetOrganization(
            new OrganizationSummaryDto(7, "Forge", OrganizationRole.Owner, false)));
        cut.WaitForState(() => cut.Markup.Contains("Add Project", StringComparison.Ordinal));

        Assert.Contains("api/organizations/7/projects", HttpRequests);
        Assert.Contains("Add Project", cut.Markup);
    }

    [Fact]
    public async Task Project_management_ignores_delayed_response_from_previous_organization()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseFactoryAsync = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/organizations/1/projects", StringComparison.Ordinal))
                return delayed.Task;
            return Task.FromResult(Json(Array.Empty<Project>()));
        };
        var state = Services.GetRequiredService<OrganizationStateService>();
        state.SetOrganization(new OrganizationSummaryDto(1, "A", OrganizationRole.Owner, false));
        var cut = Render<ProjectManagement>();
        cut.WaitForState(() => HttpRequests.Contains("api/organizations/1/projects"));

        await cut.InvokeAsync(() => state.SetOrganization(
            new OrganizationSummaryDto(2, "B", OrganizationRole.Member, false)));
        cut.WaitForState(() => HttpRequests.Contains("api/organizations/2/projects"));
        delayed.SetResult(Json(new[] { new Project { ProjectId = 10, ProjectName = "A project" } }));
        await cut.InvokeAsync(() => Task.Delay(10));

        Assert.DoesNotContain("A project", cut.Markup);
        Assert.DoesNotContain("Add Project", cut.Markup);
        Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
    }

    [Fact]
    public void Organization_members_ignores_delayed_load_from_previous_organization()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseFactoryAsync = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/organizations/1/members", StringComparison.Ordinal))
                return delayed.Task;
            if (path.EndsWith("/organizations/2/members", StringComparison.Ordinal))
                return Task.FromResult(Json(new[]
                {
                    new OrganizationMemberDto(2, "member-b", OrganizationRole.Member, DateTime.UtcNow)
                }));
            return Task.FromResult(Json(Array.Empty<OrganizationInvitationDto>()));
        };
        var cut = Render<OrganizationMembers>(parameters => parameters
            .Add(component => component.OrganizationId, 1)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => HttpRequests.Contains("api/organizations/1/members"));

        cut.Render(parameters => parameters
            .Add(component => component.OrganizationId, 2)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("member-b", StringComparison.Ordinal));
        delayed.SetResult(Json(new[]
        {
            new OrganizationMemberDto(1, "member-a", OrganizationRole.Member, DateTime.UtcNow)
        }));
        cut.WaitForAssertion(() => Assert.DoesNotContain("member-a", cut.Markup));

        Assert.Contains("member-b", cut.Markup);
        Assert.Contains("api/organizations/2/invitations", HttpRequests);
        Assert.DoesNotContain("api/organizations/1/invitations", HttpRequests);
    }

    [Fact]
    public void Project_access_ignores_delayed_load_from_previous_project()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseFactoryAsync = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return path.EndsWith("/projects/1/members", StringComparison.Ordinal)
                ? delayed.Task
                : Task.FromResult(Json(new[]
                {
                    new ProjectMemberAccessDto("member-b", OrganizationRole.Member, false)
                }));
        };
        var cut = Render<ProjectAccessEditor>(parameters => parameters
            .Add(component => component.ProjectId, 1)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => HttpRequests.Contains("api/projects/1/members"));

        cut.Render(parameters => parameters
            .Add(component => component.ProjectId, 2)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("member-b", StringComparison.Ordinal));
        delayed.SetResult(Json(new[]
        {
            new ProjectMemberAccessDto("member-a", OrganizationRole.Member, false)
        }));
        cut.WaitForAssertion(() => Assert.DoesNotContain("member-a", cut.Markup));

        Assert.Contains("member-b", cut.Markup);
    }

    [Fact]
    public async Task Organization_members_ignores_delayed_mutation_after_organization_changes()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseFactoryAsync = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("/organizations/1/", StringComparison.Ordinal))
                return delayed.Task;
            if (path.EndsWith("/invitations", StringComparison.Ordinal))
                return Task.FromResult(Json(Array.Empty<OrganizationInvitationDto>()));
            var organizationId = path.Contains("/organizations/1/", StringComparison.Ordinal) ? 1 : 2;
            return Task.FromResult(Json(new[]
            {
                new OrganizationMemberDto(
                    organizationId,
                    organizationId == 1 ? "member-a" : "member-b",
                    OrganizationRole.Member,
                    DateTime.UtcNow)
            }));
        };
        var cut = Render<OrganizationMembers>(parameters => parameters
            .Add(component => component.OrganizationId, 1)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("member-a", StringComparison.Ordinal));
        cut.Find("input[type='checkbox']").Change(true);
        var mutation = cut.Find("button[data-action='promote']").ClickAsync(new());

        cut.Render(parameters => parameters
            .Add(component => component.OrganizationId, 2)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("member-b", StringComparison.Ordinal));
        delayed.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await mutation;

        Assert.Contains("member-b", cut.Markup);
        Assert.DoesNotContain("member-a", cut.Markup);
        Assert.DoesNotContain("Unable", cut.Markup);
        Assert.Single(HttpRequestLog, request =>
            request.Method == HttpMethod.Get && request.Url == "api/organizations/1/members");
    }

    [Fact]
    public async Task Project_access_ignores_delayed_mutation_after_project_changes()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseFactoryAsync = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.Contains("/projects/1/", StringComparison.Ordinal))
                return delayed.Task;
            var projectId = path.Contains("/projects/1/", StringComparison.Ordinal) ? 1 : 2;
            return Task.FromResult(Json(new[]
            {
                new ProjectMemberAccessDto(
                    projectId == 1 ? "member-a" : "member-b",
                    OrganizationRole.Member,
                    false)
            }));
        };
        var cut = Render<ProjectAccessEditor>(parameters => parameters
            .Add(component => component.ProjectId, 1)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("member-a", StringComparison.Ordinal));
        var mutation = cut.Find("button[data-user='member-a']").ClickAsync(new());

        cut.Render(parameters => parameters
            .Add(component => component.ProjectId, 2)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("member-b", StringComparison.Ordinal));
        delayed.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await mutation;

        Assert.Contains("member-b", cut.Markup);
        Assert.DoesNotContain("member-a", cut.Markup);
        Assert.DoesNotContain("Unable", cut.Markup);
        Assert.Single(HttpRequestLog, request =>
            request.Method == HttpMethod.Get && request.Url == "api/projects/1/members");
    }

    [Fact]
    public void Members_and_project_access_do_not_reload_for_unchanged_parameters()
    {
        HttpResponseFactory = request => request.RequestUri!.AbsolutePath.Contains("/projects/", StringComparison.Ordinal)
            ? Json(Array.Empty<ProjectMemberAccessDto>())
            : request.RequestUri.AbsolutePath.EndsWith("/invitations", StringComparison.Ordinal)
                ? Json(Array.Empty<OrganizationInvitationDto>())
                : Json(Array.Empty<OrganizationMemberDto>());
        var members = Render<OrganizationMembers>(parameters => parameters
            .Add(component => component.OrganizationId, 7)
            .Add(component => component.CanManage, true));
        members.WaitForState(() => HttpRequestLog.Count == 2);
        members.Render();
        var access = Render<ProjectAccessEditor>(parameters => parameters
            .Add(component => component.ProjectId, 12)
            .Add(component => component.CanManage, true));
        access.WaitForState(() => HttpRequestLog.Count == 3);
        access.Render();

        Assert.Equal(3, HttpRequestLog.Count);
    }

    [Fact]
    public void Organization_details_preserves_dirty_name_during_unrelated_render()
    {
        var organization = new OrganizationSummaryDto(7, "Forge", OrganizationRole.Owner, false);
        var cut = Render<OrganizationDetails>(parameters =>
            parameters.Add(component => component.Organization, organization));
        cut.Find("input").Change("Edited locally");

        cut.Render();

        Assert.Equal("Edited locally", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task Organization_details_ignores_delayed_rename_after_organization_changes()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseFactoryAsync = (_, _) => delayed.Task;
        var changed = 0;
        var cut = Render<OrganizationDetails>(parameters => parameters
            .Add(component => component.Organization,
                new OrganizationSummaryDto(1, "A", OrganizationRole.Owner, false))
            .Add(component => component.OrganizationChanged,
                (OrganizationSummaryDto _) => changed++));
        cut.Find("input").Change("Renamed A");
        var mutation = cut.Find("button[data-action='rename']").ClickAsync(new());

        cut.Render(parameters => parameters
            .Add(component => component.Organization,
                new OrganizationSummaryDto(2, "B", OrganizationRole.Owner, false))
            .Add(component => component.OrganizationChanged,
                (OrganizationSummaryDto _) => changed++));
        delayed.SetResult(Json(new OrganizationSummaryDto(1, "Renamed A", OrganizationRole.Owner, false)));
        await mutation;

        Assert.Equal("B", cut.Find("input").GetAttribute("value"));
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Project_management_ignores_delayed_create_after_organization_changes()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseFactoryAsync = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post
                && path.EndsWith("/organizations/1/projects", StringComparison.Ordinal))
            {
                return delayed.Task;
            }

            return Task.FromResult(Json(Array.Empty<Project>()));
        };
        var state = Services.GetRequiredService<OrganizationStateService>();
        state.SetOrganization(new OrganizationSummaryDto(1, "A", OrganizationRole.Owner, false));
        var cut = Render<ProjectManagement>();
        cut.WaitForState(() => cut.Markup.Contains("Add Project", StringComparison.Ordinal));
        cut.Find("button[title='Add Project']").Click();
        cut.Find("input").Change("A project");
        var mutation = cut.FindAll("button").Single(button => button.TextContent.Trim() == "Save")
            .ClickAsync(new());

        await cut.InvokeAsync(() => state.SetOrganization(
            new OrganizationSummaryDto(2, "B", OrganizationRole.Member, false)));
        cut.WaitForState(() => HttpRequests.Contains("api/organizations/2/projects"));
        delayed.SetResult(Json(new Project { ProjectId = 10, ProjectName = "A project" }, HttpStatusCode.Created));
        await mutation;

        Assert.DoesNotContain("A project", cut.Markup);
        Assert.DoesNotContain("Add Project", cut.Markup);
        Assert.Null(Services.GetRequiredService<ProjectStateService>().SelectedProjectId);
    }

    [Fact]
    public async Task Project_access_transport_failure_is_shown_without_reloading()
    {
        HttpResponseFactory = request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(new[] { new ProjectMemberAccessDto("member", OrganizationRole.Member, false) });
            throw new HttpRequestException("offline");
        };
        var cut = Render<ProjectAccessEditor>(parameters => parameters
            .Add(component => component.ProjectId, 12)
            .Add(component => component.CanManage, true));
        cut.WaitForState(() => cut.Markup.Contains("member"));

        await cut.Find("button[data-user='member']").ClickAsync(new());

        Assert.Contains("Unable to update project access", cut.Markup);
        Assert.Single(HttpRequestLog.Where(request => request.Method == HttpMethod.Get));
    }
}
