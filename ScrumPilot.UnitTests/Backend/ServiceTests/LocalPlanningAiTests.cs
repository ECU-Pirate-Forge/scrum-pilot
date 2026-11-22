using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Services;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class LocalPlanningAiTests
{
    [Fact]
    public async Task ProviderFailureDoesNotUseAnExternalFallback()
    {
        var handler = new StubHandler();
        handler.Fail = true;
        var service = Service(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.GenerateAsync<Answer>("Return JSON.", new { privateSource = "private" }, default));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ValidStructuredOutputIsReadWithoutProviderFallback()
    {
        var handler = new StubHandler();
        var result = await Service(handler)
            .GenerateAsync<Answer>("Return {\"text\":\"...\"}.", new { story = "A" }, default);
        Assert.Equal("generated", result.Text);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task OversizedContextIsRejectedRatherThanTruncated()
    {
        var handler = new StubHandler();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(handler)
                .GenerateAsync<Answer>("Return JSON.", new { text = new string('a', 200 * 1024) }, default));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task FencedStructuredOutputIsAccepted()
    {
        var handler = new StubHandler { Output = "```json\n{\"text\":\"generated\"}\n```" };
        var result = await Service(handler)
            .GenerateAsync<Answer>("Return {\"text\":\"...\"}.", new { story = "A" }, default);
        Assert.Equal("generated", result.Text);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task MalformedStructuredOutputIsRetriedOnce()
    {
        var handler = new StubHandler { Outputs = ["not json", """{"text":"generated"}"""] };
        var result = await Service(handler)
            .GenerateAsync<Answer>("Return JSON.", new { story = "A" }, default);
        Assert.Equal("generated", result.Text);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task MalformedStructuredOutputFailsAfterOneRetry()
    {
        var handler = new StubHandler { Output = "not json" };
        await Assert.ThrowsAsync<JsonException>(() =>
            Service(handler).GenerateAsync<Answer>("Return JSON.", new { story = "A" }, default));
        Assert.Equal(2, handler.RequestCount);
    }

    private static LocalPlanningAi Service(StubHandler handler) =>
        new(handler, new AiOptions { Model = "local-model" });

    public sealed record Answer(string Text);

    private sealed class StubHandler : IChatClient
    {
        public int RequestCount { get; private set; }
        public bool Fail { get; set; }
        public string Output { get; set; } = """{"text":"generated"}""";
        public string[]? Outputs { get; set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var index = RequestCount++;
            Assert.Equal("local-model", options!.ModelId);
            Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
            Assert.Equal(ChatRole.System, messages.First().Role);
            Assert.Equal(ChatRole.User, messages.Last().Role);
            if (Fail) throw new HttpRequestException("Synthetic local provider outage.");
            var output = Outputs is not null ? Outputs[index] : Output;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, output)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
