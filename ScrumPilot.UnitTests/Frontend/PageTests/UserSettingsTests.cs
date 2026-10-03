using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Pages;
using ScrumPilot.Web.Services;

namespace ScrumPilot.UnitTests.Frontend.PageTests;

public sealed class UserSettingsTests : BunitContext
{
    private readonly RecordingHttpHandler _handler = new();

    public UserSettingsTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://localhost/")
        });
        Services.AddScoped<ProjectStateService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Render<MudPopoverProvider>();
    }

    [Fact]
    public void LoadsOrganizationsThenProjectsForDefaultOrganizationAndShowsReadOnlyEmail()
    {
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            Email = "confirmed@example.com",
            DefaultOrganizationId = 2,
            DefaultProjectId = 22
        });
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Default Organization")
        });
        _handler.Respond("api/organizations/2/projects", new[]
        {
            Project(22, 2, "Default Project")
        });

        var cut = Render<UserSettings>();

        Assert.Contains("api/organizations", _handler.Requests);
        Assert.Contains("api/organizations/2/projects", _handler.Requests);
        Assert.DoesNotContain("api/Project", _handler.Requests);
        Assert.Contains("Default Organization", cut.Markup);
        Assert.Contains("Default Project", cut.Markup);
        var email = cut.Find("input[type=email]");
        Assert.True(email.HasAttribute("readonly"));
        Assert.Contains("confirmed-email", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangingOrganizationClearsProjectLoadsScopedProjectsAndSavesBothDefaults()
    {
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            Email = "confirmed@example.com",
            DiscordUsername = "discord",
            UiPreference = UiPreference.Dark,
            DefaultOrganizationId = 1,
            DefaultProjectId = 11
        });
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Second")
        });
        _handler.Respond("api/organizations/1/projects", new[]
        {
            Project(11, 1, "Old Project")
        });
        _handler.Respond("api/organizations/2/projects", new[]
        {
            Project(21, 2, "New Project")
        });
        _handler.RespondStatus("api/user/settings", HttpMethod.Put, HttpStatusCode.NoContent);
        var cut = Render<UserSettings>();
        var organizationSelect = cut.FindComponents<MudSelect<int?>>()
            .Single(select => select.Instance.Label == "Default Organization");

        await cut.InvokeAsync(
            () => organizationSelect.Instance.ValueChanged.InvokeAsync(2));

        Assert.Contains("api/organizations/2/projects", _handler.Requests);
        var projectSelect = cut.FindComponents<MudSelect<int?>>()
            .Single(select => select.Instance.Label == "Default Project");
        Assert.Null(projectSelect.Instance.Value);

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save Preferences"))
            .Click();

        var body = Assert.Single(_handler.JsonBodies);
        Assert.Equal(2, body.DefaultOrganizationId);
        Assert.Null(body.DefaultProjectId);
        Assert.Equal("discord", body.DiscordUsername);
        Assert.Equal(UiPreference.Dark, body.UiPreference);
        Assert.Contains("Settings saved successfully.", cut.Markup);
    }

    [Fact]
    public void SaveDisplaysServerValidationError()
    {
        _handler.Respond("api/user/settings", new UserSettingsDto
        {
            Email = "confirmed@example.com"
        });
        _handler.Respond("api/organizations", Array.Empty<OrganizationSummaryDto>());
        _handler.RespondJson(
            "api/user/settings",
            HttpMethod.Put,
            HttpStatusCode.BadRequest,
            new { errors = new[] { "The selected defaults are not available." } });
        var cut = Render<UserSettings>();

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save Preferences"))
            .Click();

        Assert.Contains("The selected defaults are not available.", cut.Markup);
    }

    [Fact]
    public async Task ChangingOrganizationIgnoresProjectsFromOlderRequestCompletingLast()
    {
        _handler.Respond("api/user/settings", new UserSettingsDto());
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Second")
        });
        var first = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.RespondAsync("api/organizations/1/projects", _ => first.Task);
        _handler.RespondAsync("api/organizations/2/projects", _ => second.Task);
        var cut = Render<UserSettings>();
        var organizationSelect = cut.FindComponents<MudSelect<int?>>()
            .Single(select => select.Instance.Label == "Default Organization");

        var firstChange = cut.InvokeAsync(
            () => organizationSelect.Instance.ValueChanged.InvokeAsync(1));
        Assert.Contains("api/organizations/1/projects", _handler.Requests);
        var secondChange = cut.InvokeAsync(
            () => organizationSelect.Instance.ValueChanged.InvokeAsync(2));
        second.SetResult(JsonResponse(new[] { Project(21, 2, "Second Project") }));
        await secondChange;
        var projectSelect = cut.FindComponents<MudSelect<int?>>()
            .Single(select => select.Instance.Label == "Default Project");
        await cut.InvokeAsync(
            () => projectSelect.Instance.ValueChanged.InvokeAsync(21));
        first.SetResult(JsonResponse(new[] { Project(11, 1, "Stale Project") }));
        await firstChange;

        Assert.Contains("Second Project", cut.Markup);
        Assert.DoesNotContain("Stale Project", cut.Markup);
    }

    [Fact]
    public async Task ChangingOrganizationIgnoresErrorFromOlderRequest()
    {
        _handler.Respond("api/user/settings", new UserSettingsDto());
        _handler.Respond("api/organizations", new[]
        {
            Organization(1, "First"),
            Organization(2, "Second")
        });
        var first = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.RespondAsync("api/organizations/1/projects", _ => first.Task);
        _handler.RespondAsync("api/organizations/2/projects", _ => second.Task);
        var cut = Render<UserSettings>();
        var organizationSelect = cut.FindComponents<MudSelect<int?>>()
            .Single(select => select.Instance.Label == "Default Organization");

        var firstChange = cut.InvokeAsync(
            () => organizationSelect.Instance.ValueChanged.InvokeAsync(1));
        var secondChange = cut.InvokeAsync(
            () => organizationSelect.Instance.ValueChanged.InvokeAsync(2));
        second.SetResult(JsonResponse(new[] { Project(21, 2, "Second Project") }));
        await secondChange;
        var projectSelect = cut.FindComponents<MudSelect<int?>>()
            .Single(select => select.Instance.Label == "Default Project");
        await cut.InvokeAsync(
            () => projectSelect.Instance.ValueChanged.InvokeAsync(21));
        first.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await firstChange;

        Assert.Contains("Second Project", cut.Markup);
        Assert.DoesNotContain(
            "Failed to load projects for the selected organization.",
            cut.Markup);
    }

    private static OrganizationSummaryDto Organization(int id, string name) =>
        new(id, name, OrganizationRole.Member, false);

    private static Project Project(int id, int organizationId, string name) =>
        new()
        {
            ProjectId = id,
            OrganizationId = organizationId,
            ProjectName = name
        };

    private static HttpResponseMessage JsonResponse<T>(T value) where T : notnull =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class RecordingHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<(HttpMethod Method, string Route),
            Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();

        public List<string> Requests { get; } = [];
        public List<UserSettingsDto> JsonBodies { get; } = [];

        public void Respond<T>(string route, T value) where T : notnull =>
            RespondJson(route, HttpMethod.Get, HttpStatusCode.OK, value);

        public void RespondStatus(string route, HttpMethod method, HttpStatusCode statusCode) =>
            _responses[(method, route)] = _ =>
                Task.FromResult(new HttpResponseMessage(statusCode));

        public void RespondAsync(
            string route,
            Func<CancellationToken, Task<HttpResponseMessage>> response) =>
            _responses[(HttpMethod.Get, route)] = response;

        public void RespondJson<T>(
            string route,
            HttpMethod method,
            HttpStatusCode statusCode,
            T value) where T : notnull =>
            _responses[(method, route)] = _ => Task.FromResult(
                new HttpResponseMessage(statusCode) { Content = JsonContent.Create(value) });

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var route = request.RequestUri!.PathAndQuery.TrimStart('/');
            Requests.Add(route);
            if (request.Method == HttpMethod.Put && request.Content is not null)
            {
                JsonBodies.Add((await request.Content.ReadFromJsonAsync<UserSettingsDto>(
                    cancellationToken))!);
            }

            return _responses.TryGetValue((request.Method, route), out var response)
                ? await response(cancellationToken)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
