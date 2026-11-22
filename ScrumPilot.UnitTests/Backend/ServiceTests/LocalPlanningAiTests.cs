using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.AI;
using OllamaSharp;
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
        Assert.Null(handler.ResponseFormat);
    }

    [Fact]
    public async Task StoryGenerationSendsItsJsonSchemaToOllama()
    {
        using var handler = new OllamaHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var client = new OllamaApiClient(http);
        var service = new LocalPlanningAi(client, new AiOptions { Model = "local-model" });

        var result = await service.GenerateStoryAsync<ProjectStoryAiOutput>("Return stories.", new { }, default);

        Assert.Equal("Generated story", Assert.Single(result.Stories).Title);
        Assert.Equal(1, handler.RequestCount);
        using var request = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        var format = request.RootElement.GetProperty("format");
        Assert.Equal("object", format.GetProperty("type").GetString());
        Assert.False(format.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(format.GetProperty("required").EnumerateArray(), p => p.GetString() == "stories");
        var stories = format.GetProperty("properties").GetProperty("stories");
        Assert.Equal("array", stories.GetProperty("type").GetString());
        var item = stories.GetProperty("items");
        Assert.False(item.GetProperty("additionalProperties").GetBoolean());
        var properties = item.GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("description").GetProperty("type").GetString());
        Assert.Contains(properties.GetProperty("priority").GetProperty("enum").EnumerateArray(),
            p => p.GetString() == "Medium");
        Assert.Contains(properties.GetProperty("storyPoints").GetProperty("enum").EnumerateArray(),
            p => p.GetString() == "TwentyOne");
    }

    [Theory]
    [InlineData("{private-content", "Local AI returned malformed JSON.")]
    [InlineData("""{"text":123}""", "Local AI returned JSON that does not match the expected schema.")]
    [InlineData("""{"text":"ok","private-content":true}""", "Local AI returned JSON that does not match the expected schema.")]
    public async Task InvalidJsonProducesSafeSpecificMessage(string output, string expectedMessage)
    {
        var handler = new StubHandler { Output = output };
        var error = await Assert.ThrowsAnyAsync<JsonException>(() =>
            Service(handler).GenerateStoryAsync<Answer>("Return JSON.", new { privateSource = "private" }, default));
        Assert.Equal(expectedMessage, error.Message);
        Assert.DoesNotContain("private-content", error.Message);
    }

    [Theory]
    [InlineData("""{"stories":"private-content"}""", "The stories field must be an array.")]
    [InlineData("""{"stories":[{"title":"Test","description":"Test","priority":"private-content","storyPoints":"Zero"}]}""",
        "A generated story has an invalid priority.")]
    [InlineData("""{"stories":[{"title":"Test","description":"Test","priority":"Low","storyPoints":"private-content"}]}""",
        "A generated story has an invalid story-point value.")]
    public async Task StoryJsonReportsKnownInvalidFieldsWithoutTheirValues(string output, string expectedMessage)
    {
        var error = await Assert.ThrowsAnyAsync<JsonException>(() => Service(new StubHandler { Output = output })
            .GenerateStoryAsync<ProjectStoryAiOutput>("Return JSON.", new { }, default));
        Assert.Equal(expectedMessage, error.Message);
        Assert.DoesNotContain("private-content", error.Message);
    }

    [Fact]
    public async Task OtherPlanningFlowsRetainJsonExceptionType()
    {
        await Assert.ThrowsAsync<JsonException>(() => Service(new StubHandler { Output = "not json" })
            .GenerateAsync<Answer>("Return JSON.", new { }, default));
    }

    [Fact]
    public async Task TaskGenerationRequestsItsJsonSchemaFromTheProvider()
    {
        var handler = new StubHandler { Output = """{"tasks":[]}""" };
        await Service(handler).GenerateStructuredAsync<TaskGenerationOutput>("Return task proposals.", new { story = "A" }, default);

        var format = Assert.IsType<ChatResponseFormatJson>(handler.ResponseFormat);
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

    [Fact]
    public async Task OversizedStoryContextHasSpecificFailureWithoutCallingProvider()
    {
        var handler = new StubHandler();
        await Assert.ThrowsAsync<LocalPlanningAi.InputTooLargeException>(() =>
            Service(handler).GenerateStoryAsync<Answer>("Return JSON.", new { text = new string('a', 200 * 1024) }, default));
        Assert.Equal(0, handler.RequestCount);
    }


    private static LocalPlanningAi Service(StubHandler handler) =>
        new(handler, new AiOptions { Model = "local-model" });

    public sealed record Answer(string Text);

    private sealed class OllamaHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var content = JsonSerializer.Serialize(new
            {
                model = "local-model",
                created_at = "2026-01-01T00:00:00Z",
                message = new
                {
                    role = "assistant",
                    content = """{"stories":[{"title":"Generated story","description":"An observable outcome.","priority":"Medium","storyPoints":"Zero"}]}"""
                },
                done = true,
                done_reason = "stop"
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content + "\n", Encoding.UTF8, "application/x-ndjson")
            };
        }
    }

    private sealed class StubHandler : IChatClient
    {
        public int RequestCount { get; private set; }
        public ChatResponseFormat? ResponseFormat { get; private set; }
        public bool Fail { get; set; }
        public string Output { get; set; } = """{"text":"generated"}""";
        public string SystemMessage { get; private set; } = "";
        public string UserMessage { get; private set; } = "";
        public string[]? Outputs { get; set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var index = RequestCount++;
            ResponseFormat = options?.ResponseFormat;
            Assert.Equal("local-model", options!.ModelId);
            Assert.Equal(ChatRole.System, messages.First().Role);
            Assert.Equal(ChatRole.User, messages.Last().Role);
            SystemMessage = messages.First().Text;
            UserMessage = messages.Last().Text;
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
