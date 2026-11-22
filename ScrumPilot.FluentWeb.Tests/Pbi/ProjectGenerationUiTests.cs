using System.Text.Json;
using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ScrumPilot.FluentWeb.Services;
using ScrumPilot.FluentWeb.Components.Pbi;
using ScrumPilot.FluentWeb.Components.ProjectManagement;
using ScrumPilot.FluentWeb.Tests.TestHelpers;
using ScrumPilot.Shared.Models;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Pbi;

public sealed class ProjectGenerationUiTests : FluentFrontendTestBase
{
    [Theory]
    [InlineData(OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Member, false)]
    public void ApplicabilityReview_IsOwnerOnlyAndPostsExactFingerprint(OrganizationRole role, bool canReview)
    {
        Services.GetRequiredService<OrganizationStateService>().SetOrganization(new(1, "Project organization", role, false));
        var currency = new PlanningArtifactCurrency(true, true, true, true, false, true, 1)
        { CurrentFingerprint = new string('a', 64) };
        Http.On(HttpMethod.Post, "api/projects/17/story-generation/stories/101/review", (_, _) =>
            MockHttpHandler.Json(currency with
            {
                SourceChanged = false,
                GuidelinesChanged = false,
                ApplicabilityReviewed = true,
                ApplicabilityReview = new()
                {
                    ProjectId = 17,
                    PbiId = 101,
                    Fingerprint = currency.CurrentFingerprint,
                    ActorId = "owner",
                    Reason = "<script>retained safely</script>",
                    CreatedAt = DateTime.UtcNow
                }
            }));
        using var cut = Render<StoryProvenancePanel>(p => p.Add(c => c.Pbi,
            new ProductBacklogItem { ProjectId = 17, PbiId = 101, Title = "Retained story", ProblemStatementId = 9 }).Add(c => c.Currency, currency));
        Assert.Equal(canReview ? 1 : 0, cut.FindAll("[data-testid=applicability-review-reason]").Count);
        if (!canReview) return;
        cut.Find("[data-testid=applicability-review-reason]").Change("Requirements still apply.");
        cut.FindAll("button, fluent-button").First(b => b.TextContent.Trim() == "Confirm content applicability").Click();
        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/projects/17/story-generation/stories/101/review"));
            var payload = JsonSerializer.Deserialize<ReviewStoryApplicabilityRequest>(request.Body!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(currency.CurrentFingerprint, payload.ExpectedFingerprint);
            Assert.Equal("Requirements still apply.", payload.Reason);
            Assert.Contains("Owner reviewed content applicability", cut.Markup);
            Assert.Contains("dependencies need review", cut.Markup);
            Assert.Empty(cut.FindAll("script"));
            Assert.Contains("&lt;script&gt;", cut.Markup);
        });
    }

    [Fact]
    public void ApplicabilityReview_ConflictRetainsReasonAndRequestsRefresh()
    {
        Services.GetRequiredService<OrganizationStateService>().SetOrganization(new(1, "Project organization", OrganizationRole.Owner, false));
        Http.OnStatus(HttpMethod.Post, "api/projects/17/story-generation/stories/101/review", HttpStatusCode.Conflict);
        using var cut = Render<StoryProvenancePanel>(p => p.Add(c => c.Pbi,
            new ProductBacklogItem { ProjectId = 17, PbiId = 101, Title = "Retained story", ProblemStatementId = 9 })
            .Add(c => c.Currency, new PlanningArtifactCurrency(true, true, true, false, false, false, 1)
            { CurrentFingerprint = new string('b', 64) }));
        cut.Find("[data-testid=applicability-review-reason]").Change("My unsaved review.");
        cut.FindAll("button, fluent-button").First(b => b.TextContent.Trim() == "Confirm content applicability").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Refresh provenance and review again", cut.Markup);
            Assert.Contains("My unsaved review.", cut.Markup);
            Assert.DoesNotContain("Owner reviewed content applicability", cut.Markup);
        });
    }

    [Fact]
    public void MemberGuidelinesPreviewEncodesMarkupAndHasNoSaveButton()
    {
        Http.OnGetJson("api/projects/17/guidelines", new ProjectGuidelinesRevision
        { ProjectGuidelinesRevisionId = 3, ProjectId = 17, Revision = 1, ActorId = "owner", Content = "<script>alert(1)</script>" });
        Http.OnGetJson("api/projects/17/guidelines/history", Array.Empty<ProjectGuidelinesRevision>());
        using var cut = Render<ProjectGuidelinesPanel>(p => p.Add(c => c.ProjectId, 17).Add(c => c.IsOwner, false));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("&lt;script&gt;", cut.Markup);
            Assert.Empty(cut.FindAll("script"));
            Assert.Empty(cut.FindAll("textarea"));
            Assert.DoesNotContain("Save guidelines", cut.Markup);
        });
    }

    [Fact]
    public void ProjectProposalAcceptanceUsesServerIdentityAndNeverLegacyCreate()
    {
        var pbi = new ProductBacklogItem
        {
            ProjectId = 17,
            PbiId = 0,
            Title = "Sourced",
            Description = "Reviewed",
            ProblemStatementId = 9,
            GenerationRunId = 7,
            GenerationCandidateKey = "story-1"
        };
        Http.On(HttpMethod.Post, "api/projects/17/story-generation/accept", (_, _) => MockHttpHandler.Json(new[] {
            new ProductBacklogItem { ProjectId = 17, PbiId = 101, Title = "Edited preview", Description = "Reviewed",
                ProblemStatementId = 9, GenerationRunId = 7, GenerationCandidateKey = "story-1", Version = 1 }
        }));
        using var cut = Render<GeneratedPbiDialog>(p => p
            .Add(c => c.Open, true).Add(c => c.ProjectId, 17).Add(c => c.PBIs, [pbi])
            .Add(c => c.Proposals, [new(1, 9, 7, pbi, new(true, true, false, false, false, false, 3))]));
        cut.Find("[data-testid=project-proposal-title]").Change("Edited preview");
        cut.FindAll("button, fluent-button").First(b => b.TextContent.Trim() == "Add to Backlog").Click();
        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/projects/17/story-generation/accept"));
            var payload = JsonSerializer.Deserialize<AcceptProjectStoriesRequest>(request.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(7, payload.GenerationRunId);
            Assert.Equal(1, Assert.Single(payload.Items).ProposalId);
            Assert.Equal("Edited preview", payload.Items[0].Title);
            Assert.Empty(Http.RequestsTo(HttpMethod.Post, "api/Pbi/createStories"));
            Assert.Contains("All generated PBIs have been saved", cut.Markup);
        });
    }

    [Fact]
    public void RemainingAcceptanceUsesSnapshotIdsAndExcludesAlreadyAcceptedCandidate()
    {
        var first = new ProductBacklogItem
        {
            PbiId = 201,
            ProjectId = 17,
            Title = "Same title",
            ProblemStatementId = 9,
            GenerationRunId = 7,
            GenerationCandidateKey = "story-1",
            Version = 1
        };
        var second = new ProductBacklogItem
        {
            ProjectId = 17,
            Title = "Same title",
            ProblemStatementId = 9,
            GenerationRunId = 7,
            GenerationCandidateKey = "story-2"
        };
        Http.On(HttpMethod.Post, "api/projects/17/story-generation/accept", (_, _) => MockHttpHandler.Json(new[] {
            new ProductBacklogItem { PbiId = 202, ProjectId = 17, Title = "Same title",
                ProblemStatementId = 9, GenerationRunId = 7, GenerationCandidateKey = "story-2", Version = 1 }
        }));
        var currency = new PlanningArtifactCurrency(true, true, false, false, false, false, null);
        using var cut = Render<GeneratedPbiDialog>(p => p.Add(c => c.Open, true).Add(c => c.ProjectId, 17)
            .Add(c => c.PBIs, [first, second])
            .Add(c => c.Proposals, [new(1, 9, 7, first, currency, 201), new(2, 9, 7, second, currency)]));
        cut.FindAll("button, fluent-button").First(b => b.TextContent.Contains("Add Remaining to Backlog")).Click();
        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Post, "api/projects/17/story-generation/accept"));
            var payload = JsonSerializer.Deserialize<AcceptProjectStoriesRequest>(request.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(2, Assert.Single(payload.Items).ProposalId);
            Assert.Equal(0, payload.Items[0].ExpectedVersion);
            Assert.Contains("All generated PBIs have been saved", cut.Markup);
        });
    }

    [Fact]
    public void OwnerSaveSendsExpectedRevisionAndConflictRetainsUnsavedEdits()
    {
        Http.OnGetJson("api/projects/17/guidelines", new ProjectGuidelinesRevision
        { ProjectGuidelinesRevisionId = 3, ProjectId = 17, Revision = 1, ActorId = "owner", Content = "Saved" });
        Http.OnGetJson("api/projects/17/guidelines/history", Array.Empty<ProjectGuidelinesRevision>());
        Http.OnStatus(HttpMethod.Put, "api/projects/17/guidelines", HttpStatusCode.Conflict);
        using var cut = Render<ProjectGuidelinesPanel>(p => p.Add(c => c.ProjectId, 17).Add(c => c.IsOwner, true));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("textarea")));
        cut.Find("textarea").Change("Unsaved work");
        cut.FindAll("button, fluent-button").First(b => b.TextContent.Trim() == "Save guidelines").Click();
        cut.WaitForAssertion(() =>
        {
            var request = Assert.Single(Http.RequestsTo(HttpMethod.Put, "api/projects/17/guidelines"));
            var payload = JsonSerializer.Deserialize<SaveProjectGuidelinesRequest>(request.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(3, payload.ExpectedRevisionId);
            Assert.Equal("Unsaved work", payload.Content);
            Assert.Contains("Copy your edits", cut.Markup);
            Assert.Contains("Unsaved work", cut.Markup);
        });
    }

    [Fact]
    public async Task ProjectGuidelinesCopyPassesCurrentAndPreviewTextToClipboard()
    {
        Http.OnGetJson("api/projects/17/guidelines", new ProjectGuidelinesRevision
        { ProjectGuidelinesRevisionId = 3, ProjectId = 17, Revision = 1, ActorId = "owner", Content = "Saved guidance" });
        Http.OnGetJson("api/projects/17/guidelines/history", new[]
        {
            new ProjectGuidelinesRevision
            { ProjectGuidelinesRevisionId = 2, ProjectId = 17, Revision = 1, ActorId = "owner", Content = "Historical guidance" }
        });
        using var cut = Render<ProjectGuidelinesPanel>(p => p.Add(c => c.ProjectId, 17).Add(c => c.IsOwner, true));
        cut.WaitForAssertion(() => Assert.Contains("Saved guidance", cut.Markup));

        await cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Copy guidelines").ClickAsync(new());
        var copy = Assert.Single(JSInterop.Invocations, x => x.Identifier == "mermaidHelper.copyText");
        Assert.Equal("Saved guidance", copy.Arguments[0]);

        await cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Copy revision 1").ClickAsync(new());
        var revisionCopy = JSInterop.Invocations.Where(x => x.Identifier == "mermaidHelper.copyText").Last();
        Assert.Equal("Historical guidance", revisionCopy.Arguments[0]);
    }

    [Fact]
    public void LinkedStoryCardLoadsCurrencyWithoutChangingContent()
    {
        Http.OnGetJson("api/projects/17/story-generation/stories/101/currency",
            new PlanningArtifactCurrency(true, true, true, true, false, false, 1));
        using var cut = Render<PbiCard>(p => p.Add(c => c.PbiModel, new ProductBacklogItem
        { ProjectId = 17, PbiId = 101, Title = "Original retained", ProblemStatementId = 9, GenerationRunId = 7 }));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("source changed", cut.Markup);
            Assert.Contains("guidelines changed", cut.Markup);
            Assert.Contains("Original retained", cut.Markup);
        });
    }
}
