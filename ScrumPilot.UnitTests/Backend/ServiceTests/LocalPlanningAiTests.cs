using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
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
    public async Task TaskGenerationRequestsItsJsonSchemaFromTheProvider()
    {
        var handler = new StubHandler { Output = """{"tasks":[]}""" };
        await Service(handler).GenerateAsync<TaskGenerationOutput>("Return task proposals.", new { story = "A" }, default);

        var format = Assert.IsType<ChatResponseFormatJson>(handler.Options!.ResponseFormat);
        Assert.True(format.Schema.HasValue);
        var schema = format.Schema.Value;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("tasks", schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        var tasks = schema.GetProperty("properties").GetProperty("tasks");
        Assert.Equal("array", tasks.GetProperty("type").GetString());
        var task = tasks.GetProperty("items");
        Assert.False(task.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["key", "parentStoryId", "title", "content", "files", "dependsOnKeys", "existingPrerequisites"],
            task.GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.Equal("object", task.GetProperty("properties").GetProperty("content").GetProperty("type").GetString());
        var file = task.GetProperty("properties").GetProperty("files").GetProperty("items");
        Assert.Contains("Source", file.GetProperty("properties").GetProperty("category").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()));
    }

    [Theory]
    [InlineData("```json\n{\"text\":\"generated\"}\n```")]
    [InlineData("{\"text\":\"generated\"}\nHere is my plan.")]
    [InlineData("Here is the JSON:\n{\"text\":\"generated\"}")]
    public async Task NonJsonResponsesAreRejectedWithoutRepairOrRetry(string output)
    {
        var handler = new StubHandler { Output = output };

        await Assert.ThrowsAsync<JsonException>(() =>
            Service(handler).GenerateAsync<Answer>("Return JSON.", new { story = "A" }, default));

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

    private static LocalPlanningAi Service(StubHandler handler) =>
        new(handler, new AiOptions { Model = "local-model" });

    public sealed record Answer(string Text);

    private sealed class StubHandler : IChatClient
    {
        public int RequestCount { get; private set; }
        public bool Fail { get; set; }
        public string Output { get; set; } = """{"text":"generated"}""";
        public ChatOptions? Options { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            Options = options;
            Assert.Equal("local-model", options!.ModelId);
            Assert.Equal(ChatRole.System, messages.First().Role);
            Assert.Equal(ChatRole.User, messages.Last().Role);
            if (Fail) throw new HttpRequestException("Synthetic local provider outage.");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Output)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
