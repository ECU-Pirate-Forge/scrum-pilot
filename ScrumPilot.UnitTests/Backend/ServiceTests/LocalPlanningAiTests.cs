using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using ScrumPilot.API.Services;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class LocalPlanningAiTests
{
    [Theory]
    [InlineData("https://external.example/")]
    [InlineData("file:///C:/models")]
    public async Task PrivateContextRejectsExternalProvidersBeforeSending(string endpoint)
    {
        var handler = new StubHandler();
        var service = Service(endpoint, handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAsync<Answer>("Return JSON.", new { privateSource = "private" }, default));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ValidStructuredOutputIsReadWithoutProviderFallback()
    {
        var handler = new StubHandler();
        var result = await Service("http://localhost:11434/", handler)
            .GenerateAsync<Answer>("Return {\"text\":\"...\"}.", new { story = "A" }, default);
        Assert.Equal("generated", result.Text);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task OversizedContextIsRejectedRatherThanTruncated()
    {
        var handler = new StubHandler();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service("http://localhost:11434/", handler)
                .GenerateAsync<Answer>("Return JSON.", new { text = new string('a', 200 * 1024) }, default));
        Assert.Equal(0, handler.RequestCount);
    }

    private static LocalPlanningAi Service(string endpoint, StubHandler handler) =>
        new(new HttpClient(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OllamaBaseUrl"] = endpoint,
            ["OllamaModel"] = "local-model",
            ["GroqApiKey"] = "not-used"
        }).Build());

    public sealed record Answer(string Text);

    private sealed class StubHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.True(request.RequestUri!.IsLoopback);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"response\":\"{\\\"text\\\":\\\"generated\\\"}\"}", Encoding.UTF8, "application/json")
            });
        }
    }
}
