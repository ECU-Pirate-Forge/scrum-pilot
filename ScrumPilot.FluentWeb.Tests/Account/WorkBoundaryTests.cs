using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.Client;
using ScrumPilot.FluentWeb.Layout;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.FluentWeb.Tests.TestHelpers;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Account;

public sealed class WorkBoundaryTests : FluentFrontendTestBase
{
    [Theory]
    [InlineData("organization")]
    [InlineData("project")]
    [InlineData("theme")]
    public async Task Hydrated_selected_menu_values_do_not_navigate_remount_or_write_settings(string control)
    {
        RegisterSession();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/organizations/1/backlog?projectId=7");
        using var cut = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<ScopeProbe>(0);
            builder.CloseComponent();
        }));
        var scope = cut.FindComponent<ScopeProbe>().Instance.Work;
        var originalUri = navigation.Uri;
        var label = control switch
        {
            "organization" => "First",
            "project" => "Project 1",
            _ => Services.GetRequiredService<AppThemeService>().Preference.ToString()
        };
        var selected = cut.FindAll("fluent-menu-item").Single(item => item.TextContent.Trim() == label);

        await selected.TriggerEventAsync("onmenuitemchange", new Microsoft.FluentUI.AspNetCore.Components.MenuItemEventArgs { Id = selected.Id, Checked = true });

        Assert.Equal(originalUri, navigation.Uri);
        Assert.Same(scope, cut.FindComponent<ScopeProbe>().Instance.Work);
        Assert.True(scope.IsCurrent);
        Assert.Empty(Http.RequestsTo(HttpMethod.Put, "api/user/settings"));
    }

    [Fact]
    public async Task Independent_tabs_keep_their_own_scope_without_writing_defaults()
    {
        using var otherTab = new WorkBoundaryTests();
        RegisterSession();
        otherTab.RegisterSession();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/organizations/1/backlog?projectId=7");
        otherTab.Services.GetRequiredService<NavigationManager>().NavigateTo("/organizations/2/backlog?projectId=7");
        using var first = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<ScopeProbe>(0);
            builder.CloseComponent();
        }));
        using var second = otherTab.Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<ScopeProbe>(0);
            builder.CloseComponent();
        }));
        var secondScope = second.FindComponent<ScopeProbe>().Instance.Work;

        await first.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/organizations/2/backlog?projectId=7"));

        Assert.True(secondScope.IsCurrent);
        Assert.Same(secondScope, second.FindComponent<ScopeProbe>().Instance.Work);
        Assert.Equal(2, otherTab.Services.GetRequiredService<OrganizationStateService>().SelectedOrganizationId);
        Assert.Empty(Http.RequestsTo(HttpMethod.Put, "api/user/settings"));
        Assert.Empty(otherTab.Http.RequestsTo(HttpMethod.Put, "api/user/settings"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Url_wins_over_defaults_and_switching_equal_local_ids_does_not_save_settings(int organizationId)
    {
        RegisterSession();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/organizations/{organizationId}/backlog?projectId=7");
        using var cut = Render<MainLayout>(p => p.Add(x => x.Body, builder =>
        {
            builder.OpenComponent<ScopeProbe>(0);
            builder.CloseComponent();
        }));
        cut.WaitForAssertion(() => Assert.Contains($"{organizationId}/7", cut.Markup));
        var old = cut.FindComponent<ScopeProbe>().Instance.Work;
        var next = organizationId == 1 ? 2 : 1;
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo($"/organizations/{next}/backlog?projectId=7"));
        cut.WaitForAssertion(() => Assert.Contains($"{next}/7", cut.Markup));
        Assert.False(old.IsCurrent);
        Assert.Empty(Http.RequestsTo(HttpMethod.Put, "api/user/settings"));
    }

    [Fact]
    public void Legacy_project_only_link_never_borrows_a_default_organization()
    {
        RegisterSession();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/backlog?projectId=7");
        using var cut = Render<MainLayout>();
        cut.WaitForAssertion(() => Assert.Contains("Old project-only links", cut.Markup));
        Assert.DoesNotContain(Http.Requests, request => request.Path.Contains("/projects"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void Unavailable_work_never_falls_back_to_another_tenant(HttpStatusCode status)
    {
        RegisterSession();
        Http.OnStatus(HttpMethod.Get, "api/organizations/1/projects", status);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/organizations/1/backlog?projectId=7");
        using var cut = Render<MainLayout>();
        cut.WaitForAssertion(() => Assert.Contains("Organization unavailable", cut.Markup));
        Assert.Empty(Http.RequestsTo(HttpMethod.Get, "api/organizations/2/projects"));
    }

    [Fact]
    public async Task Theme_save_preserves_the_account_default_pair()
    {
        RegisterSession();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/organizations/1/backlog?projectId=7");
        using var cut = Render<MainLayout>();
        cut.WaitForAssertion(() => Assert.Single(Http.RequestsTo(HttpMethod.Get, "api/organizations/1/projects")));
        var dark = cut.FindAll("fluent-menu-item").Single(item => item.TextContent.Trim() == "Dark");
        await dark.TriggerEventAsync("onmenuitemchange", new Microsoft.FluentUI.AspNetCore.Components.MenuItemEventArgs { Id = dark.Id, Checked = true });
        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Put, "api/user/settings"));
            var settings = JsonSerializer.Deserialize<UserSettingsDto>(request.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(2, settings.DefaultOrganizationId);
            Assert.Equal(7, settings.DefaultProjectId);
            Assert.Equal(UiPreference.Dark, settings.UiPreference);
        });
    }

    private void RegisterSession()
    {
        Http.OnGetJson("api/organizations", new List<OrganizationSummaryDto>
        {
            new(1, "First", OrganizationRole.Owner, false), new(2, "Second", OrganizationRole.Member, false)
        });
        Http.OnGetJson("api/user/settings", new UserSettingsDto { DefaultOrganizationId = 2, DefaultProjectId = 7 });
        Http.OnStatus(HttpMethod.Put, "api/user/settings", HttpStatusCode.OK);
        foreach (var id in new[] { 1, 2 })
            Http.OnGetJson($"api/organizations/{id}/projects", new List<Project> { new() { OrganizationId = id, ProjectId = 7, ProjectName = $"Project {id}" } });
    }

    public sealed class ScopeProbe : ComponentBase
    {
        [CascadingParameter] public WorkScope Work { get; set; } = default!;
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"{Work.OrganizationId}/{Work.ProjectId}");
    }
}
