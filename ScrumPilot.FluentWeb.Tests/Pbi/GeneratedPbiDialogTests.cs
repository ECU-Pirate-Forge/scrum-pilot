using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.FluentWeb.Components.Pbi;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Pbi;

public class GeneratedPbiDialogTests : FluentFrontendTestBase
{
    [Fact]
    public void Saving_one_generated_draft_posts_exactly_one_item()
    {
        Http.On(HttpMethod.Post, "api/Pbi/createDraftPbis", (req, body) => ScrumPilot.FluentWeb.Tests.TestHelpers.MockHttpHandler.Json(ReadPbis(body)));
        using var cut = Render<GeneratedPbiDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.ProjectId, 17)
            .Add(p => p.PBIs, [SamplePbi("First"), SamplePbi("Second")]));

        FindButton(cut, "Save as Draft").Click();

        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/Pbi/createDraftPbis"));
            var posted = ReadPbis(request.Body);
            var item = Assert.Single(posted);
            Assert.Equal("First", item.Title);
            Assert.Equal("?projectId=17", request.Query);
        });
    }

    [Fact]
    public void Save_remaining_excludes_items_already_saved()
    {
        Http.On(HttpMethod.Post, "api/Pbi/createDraftPbis", (req, body) => ScrumPilot.FluentWeb.Tests.TestHelpers.MockHttpHandler.Json(ReadPbis(body)));
        using var cut = Render<GeneratedPbiDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.ProjectId, 17)
            .Add(p => p.PBIs, [SamplePbi("First"), SamplePbi("Second"), SamplePbi("Third")]));

        FindButton(cut, "Save as Draft").Click();
        cut.WaitForAssertion(() => Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/Pbi/createDraftPbis")));

        FindButton(cut, "Save Remaining as Drafts").Click();

        cut.WaitForAssertion(() =>
        {
            var requests = Http.RequestsTo(HttpMethod.Post, "api/Pbi/createDraftPbis");
            Assert.Equal(2, requests.Count);
            var posted = ReadPbis(requests[1].Body);
            Assert.Equal(["Second", "Third"], posted.Select(p => p.Title).ToArray());
        });
    }

    [Fact]
    public void Failed_save_stays_pending_and_can_be_retried()
    {
        Http.OnStatus(HttpMethod.Post, "api/Pbi/createDraftPbis", HttpStatusCode.InternalServerError);
        using var cut = Render<GeneratedPbiDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.ProjectId, 17)
            .Add(p => p.PBIs, [SamplePbi("Retry me")]));

        FindButton(cut, "Save as Draft").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/Pbi/createDraftPbis"));
            Assert.Contains("Pending Review", cut.Markup);
        });

        Http.On(HttpMethod.Post, "api/Pbi/createDraftPbis", (req, body) => ScrumPilot.FluentWeb.Tests.TestHelpers.MockHttpHandler.Json(ReadPbis(body)));
        FindButton(cut, "Save as Draft").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, Http.RequestsTo(HttpMethod.Post, "api/Pbi/createDraftPbis").Count);
            Assert.Contains("Saved as Draft", cut.Markup);
        });
    }

    private static ProductBacklogItem SamplePbi(string title) => new()
    {
        ProjectId = 17,
        Title = title,
        Description = $"Description for {title}",
        Type = PbiType.Story,
        Status = PbiStatus.ToDo,
        Priority = PbiPriority.Medium,
        StoryPoints = PbiPoints.Three,
        DateCreated = DateTime.UtcNow,
        LastUpdated = DateTime.UtcNow
    };

    private static List<ProductBacklogItem> ReadPbis(string? body) =>
        JsonSerializer.Deserialize<List<ProductBacklogItem>>(body ?? "[]", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

    private static IElement FindButton(IRenderedComponent<GeneratedPbiDialog> cut, string text) =>
        cut.FindAll("button, fluent-button").First(button => button.TextContent.Contains(text, StringComparison.OrdinalIgnoreCase));
}
