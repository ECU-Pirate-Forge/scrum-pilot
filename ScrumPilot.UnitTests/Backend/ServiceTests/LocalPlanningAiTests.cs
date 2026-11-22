using System.Net;
using System.Text;
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
    public async Task ProjectGuidelinesAreAppendedToSystemMessageOnly()
    {
        var handler = new StubHandler();
        await Service(handler).GenerateAsync<Answer>("Return JSON.",
            new { story = "A", Guidelines = "Use exact tests and documentation." }, default);

        Assert.Contains("Project Guidelines:", handler.SystemMessage);
        Assert.Contains("Use exact tests and documentation.", handler.SystemMessage);
        Assert.DoesNotContain("Use exact tests and documentation.", handler.UserMessage);
        Assert.DoesNotContain("\"Guidelines\"", handler.UserMessage);
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

    private static LocalPlanningAi Service(StubHandler handler) =>
        new(handler, new AiOptions { Model = "local-model" });

    public sealed record Answer(string Text);

    private sealed class StubHandler : IChatClient
    {
        public int RequestCount { get; private set; }
        public bool Fail { get; set; }
        public string SystemMessage { get; private set; } = "";
        public string UserMessage { get; private set; } = "";
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            Assert.Equal("local-model", options!.ModelId);
            Assert.Equal(ChatRole.System, messages.First().Role);
            Assert.Equal(ChatRole.User, messages.Last().Role);
            SystemMessage = messages.First().Text;
            UserMessage = messages.Last().Text;
            if (Fail) throw new HttpRequestException("Synthetic local provider outage.");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"text":"generated"}""")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
