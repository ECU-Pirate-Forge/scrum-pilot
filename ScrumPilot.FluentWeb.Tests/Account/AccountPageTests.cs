using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using ScrumPilot.FluentWeb.Pages;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.FluentWeb.Tests.TestHelpers;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Account;

public sealed class LoginTests : FluentFrontendTestBase
{
    private readonly RecordingAuthService _auth = new(false);

    [Fact]
    public async Task Failed_login_shows_error()
    {
        using var context = new BunitContext();
        context.Services.AddFluentUIComponents();
        context.Services.AddLogging();
        context.Services.AddScoped<IAuthService>(_ => _auth);
        context.Renderer.SetRendererInfo(new RendererInfo("WebAssembly", true));
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        using var cut = context.Render<Login>();

        cut.Find("#username").Change("captain");
        cut.Find("#password").Change("wrong");
        await cut.Find("form").SubmitAsync();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Invalid username or password.", cut.Markup);
            Assert.Equal("captain", _auth.LastRequest?.UserName);
            Assert.Equal("wrong", _auth.LastRequest?.Password);
        });
    }

    private sealed class RecordingAuthService(bool result) : IAuthService
    {
        public LoginRequest? LastRequest { get; private set; }

        public Task<bool> LoginAsync(LoginRequest request)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }

        public Task LogoutAsync() => Task.CompletedTask;
    }
}

public sealed class UserSettingsTests : FluentFrontendTestBase
{
    [Fact]
    public void Saving_account_defaults_does_not_change_this_tabs_work_selection()
    {
        var projects = Services.GetRequiredService<ScrumPilot.FluentWeb.Services.ProjectStateService>();
        projects.SetProject(new Project { ProjectId = 3, OrganizationId = 1, ProjectName = "Current" });
        Http.OnGetJson("api/user/settings", new UserSettingsDto { DefaultOrganizationId = 7, DefaultProjectId = 11 });
        Http.OnGetJson("api/organizations", new List<OrganizationSummaryDto> { new(7, "Default", OrganizationRole.Owner, false) });
        Http.OnGetJson("api/organizations/7/projects", new List<Project> { new() { OrganizationId = 7, ProjectId = 11, ProjectName = "Default project" } });
        Http.OnStatus(HttpMethod.Put, "api/user/settings", HttpStatusCode.OK);
        using var cut = Render<UserSettings>();
        cut.WaitForAssertion(() => Assert.Contains("Default project", cut.Markup));
        cut.FindAll("fluent-button").Single(button => button.TextContent.Contains("Save Preferences", StringComparison.OrdinalIgnoreCase)).Click();
        cut.WaitForAssertion(() => Assert.Single(Http.RequestsTo(HttpMethod.Put, "api/user/settings")));
        Assert.Equal(3, projects.SelectedProjectId);
    }

    [Fact]
    public void User_settings_loads_and_puts_settings()
    {
        Http.OnGetJson("api/user/settings", new UserSettingsDto
        {
            Email = "user@example.test",
            UiPreference = UiPreference.System,
            DefaultOrganizationId = 7,
            DefaultProjectId = 11
        });
        Http.OnGetJson("api/organizations", new List<OrganizationSummaryDto>
        {
            new(7, "Forge", OrganizationRole.Owner, false)
        });
        Http.OnGetJson("api/organizations/7/projects", new List<Project>
        {
            new() { ProjectId = 11, OrganizationId = 7, ProjectName = "Migration" }
        });
        Http.On(HttpMethod.Put, "api/user/settings", (_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        using var cut = Render<UserSettings>();

        cut.WaitForAssertion(() => Assert.Contains("user@example.test", cut.Markup));
        cut.FindAll("fluent-button").Single(button => button.TextContent.Contains("Save Preferences", StringComparison.OrdinalIgnoreCase)).Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Put, "api/user/settings"));
            Assert.Contains("user@example.test", request.Body);
            Assert.Contains("defaultProjectId", request.Body);
        });
    }
}

public sealed class AcceptInvitationTests : FluentFrontendTestBase
{
    [Fact]
    public void Accept_invitation_posts_token_and_selects_organization()
    {
        Http.On(HttpMethod.Post, "api/organization-invitations/accept", (_, body) =>
        {
            Assert.Contains("invite-token", body);
            return MockHttpHandler.Json(new AcceptedOrganizationDto(42));
        });
        Http.OnGetJson("api/organizations", new List<OrganizationSummaryDto>
        {
            new(42, "Pirate Forge", OrganizationRole.Member, false)
        });

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/accept-invitation?token=invite-token");
        using var cut = Render<AcceptInvitation>();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/organization-invitations/accept"));
            var organizationState = Services.GetRequiredService<OrganizationStateService>();
            Assert.Equal(42, organizationState.SelectedOrganizationId);
        });
    }
}
