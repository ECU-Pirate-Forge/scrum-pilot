using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Components;
using ScrumPilot.Web.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ScrumPilot.UnitTests.Frontend.ComponentTests
{
    public class GeneratedPbiModalTests : BunitContext
    {
        private readonly RecordingHttpMessageHandler _handler = new();

        public GeneratedPbiModalTests()
        {
            Services.AddMudServices();
            Services.AddSingleton(new HttpClient(_handler)
            {
                BaseAddress = new Uri("http://localhost/")
            });
            this.AddAuthorization();
            Services.AddSingleton<ProjectStateService>();
            JSInterop.Mode = JSRuntimeMode.Loose;
            Render<MudPopoverProvider>();
        }

        [Fact]
        public void SaveAsDraft_PostsCurrentPbiAndMarksItSaved()
        {
            var component = RenderModal(CreatePbis("First", "Second"));

            FindButton(component, "Save as Draft").Click();

            component.WaitForAssertion(() =>
            {
                Assert.Contains("Saved as Draft", component.Markup);
                var request = Assert.Single(_handler.PostRequests);
                Assert.EndsWith(
                    "api/Pbi/createDraftPbis?projectId=17",
                    request.Uri,
                    StringComparison.OrdinalIgnoreCase);
                var payload = JsonSerializer.Deserialize<List<ProductBacklogItem>>(
                    request.Content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var saved = Assert.Single(payload!);
                Assert.Equal("First", saved.Title);
                Assert.Equal(17, saved.ProjectId);
            });
        }

        [Fact]
        public void BulkBacklogSave_ExcludesPreviouslySavedPbi()
        {
            var component = RenderModal(CreatePbis("First", "Second"));

            FindButton(component, "Save as Draft").Click();
            component.WaitForAssertion(() => Assert.Single(_handler.PostRequests));

            FindButton(component, "Add Remaining to Backlog").Click();

            component.WaitForAssertion(() =>
            {
                Assert.Equal(2, _handler.PostRequests.Count);
                var request = _handler.PostRequests[1];
                Assert.EndsWith(
                    "api/Pbi/createStories?projectId=17",
                    request.Uri,
                    StringComparison.OrdinalIgnoreCase);
                var payload = JsonSerializer.Deserialize<List<ProductBacklogItem>>(
                    request.Content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var saved = Assert.Single(payload!);
                Assert.Equal("Second", saved.Title);
                Assert.Contains("All generated PBIs have been saved.", component.Markup);
            });
        }

        [Fact]
        public void FailedSave_LeavesPbiPendingForRetry()
        {
            _handler.PostStatusCode = HttpStatusCode.InternalServerError;
            var component = RenderModal(CreatePbis("First"));

            FindButton(component, "Add to Backlog").Click();

            component.WaitForAssertion(() =>
            {
                Assert.Contains("Pending Review", component.Markup);
                Assert.Contains("Add to Backlog", component.Markup);
                Assert.DoesNotContain("Added to Backlog", component.Markup);
            });
        }

        [Fact]
        public void CardEdit_IsKeptInOriginalModelAndUsedByLaterBatchWithoutPersistingTwice()
        {
            var pbis = CreatePbis("First");
            var original = pbis[0];
            var component = RenderModal(pbis);

            FindButton(component, "Edit").Click();
            component.Find("input").Change("Edited before batch");
            component.FindAll("button")
                .Single(button => button.TextContent.Trim() == "Save")
                .Click();

            component.WaitForAssertion(() =>
            {
                Assert.Empty(_handler.PostRequests);
                Assert.Same(original, pbis[0]);
                Assert.Equal("Edited before batch", original.Title);
            });

            FindButton(component, "Add Remaining to Backlog").Click();

            component.WaitForAssertion(() =>
            {
                var request = Assert.Single(_handler.PostRequests);
                var payload = JsonSerializer.Deserialize<List<ProductBacklogItem>>(
                    request.Content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                Assert.Equal("Edited before batch", Assert.Single(payload!).Title);
            });
        }

        private IRenderedComponent<MudDialogProvider> RenderModal(List<ProductBacklogItem> pbis)
        {
            var provider = Render<MudDialogProvider>();
            var dialogService = Services.GetRequiredService<IDialogService>();
            var parameters = new DialogParameters
            {
                ["PBIs"] = pbis,
                ["ProjectId"] = 17
            };

            provider.InvokeAsync(() =>
                dialogService.ShowAsync<GeneratedPbiModal>("Generated PBIs", parameters));
            provider.WaitForAssertion(() => Assert.Contains("Pending Review", provider.Markup));
            return provider;
        }

        private static AngleSharp.Dom.IElement FindButton(
            IRenderedComponent<MudDialogProvider> component,
            string text) =>
            component.FindAll("button").Single(button => button.TextContent.Contains(text, StringComparison.Ordinal));

        private static List<ProductBacklogItem> CreatePbis(params string[] titles) =>
            titles.Select(title => new ProductBacklogItem
            {
                Title = title,
                Type = PbiType.Story,
                Priority = PbiPriority.Medium,
                StoryPoints = PbiPoints.Three,
                Status = PbiStatus.ToDo,
                Origin = PbiOrigin.AiGenerated,
                IsDraft = true
            }).ToList();

        private sealed class RecordingHttpMessageHandler : HttpMessageHandler
        {
            public List<RecordedRequest> PostRequests { get; } = new();
            public HttpStatusCode PostStatusCode { get; set; } = HttpStatusCode.OK;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Post)
                {
                    PostRequests.Add(new RecordedRequest(
                        request.RequestUri!.ToString(),
                        await request.Content!.ReadAsStringAsync(cancellationToken)));
                    return new HttpResponseMessage(PostStatusCode)
                    {
                        Content = new StringContent(
                            PostStatusCode == HttpStatusCode.OK ? "[]" : "Save failed",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }
        }

        private sealed record RecordedRequest(string Uri, string Content);
    }
}
