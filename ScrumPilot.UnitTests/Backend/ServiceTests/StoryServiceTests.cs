using Microsoft.Extensions.AI;
using NSubstitute;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Services;
using ScrumPilot.API.Services.AI;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class StoryServiceTests
{
    private const string Valid = """[{"type":"Story","priority":"High","storyPoints":3,"title":"Useful task","userStory":"As a user, I want a task, so that I can work.","acceptanceCriteria":["I see the task"]}]""";
    private readonly IChatClient chat = Substitute.For<IChatClient>();
    private StoryGenerationService Generator(string response = Valid)
    {
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        return new(chat, new AiOptions());
    }

    [Fact]
    public async Task Generation_UsesHostChatSeamAndConfiguredModel_ReturnsDrafts()
    {
        var stories = await Generator().GenerateAsync(["Synthetic task"]);
        var story = Assert.Single(stories);
        Assert.Equal(PbiOrigin.AiGenerated, story.Origin); Assert.True(story.IsDraft);
        Assert.Equal(PbiStatus.ToDo, story.Status);
        Assert.Contains("Acceptance Criteria", story.Description);
        await chat.Received(1).GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => o.ModelId == "gemma4:latest"), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Generation_FlattensEveryResponse()
    {
        var stories = await Generator().GenerateAsync(["First", "Second"]);
        Assert.Equal(2, stories.Count);
    }
    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[{}]")]
    [InlineData("[{\"type\":\"Story\",\"priority\":\"High\",\"storyPoints\":4,\"title\":\"X\",\"userStory\":\"Y\",\"acceptanceCriteria\":[\"Z\"]}]")]
    [InlineData("[{\"type\":0,\"priority\":\"High\",\"storyPoints\":3,\"title\":\"X\",\"userStory\":\"Y\",\"acceptanceCriteria\":[\"Z\"]}]")]
    public async Task Generation_RejectsInvalidStructuredOutput(string json) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator(json).GenerateAsync(["Task"]));
    [Fact]
    public async Task Generation_HandlesFencedStructuredArray() =>
        Assert.Single(await Generator("```json\n" + Valid + "\n```").GenerateAsync(["Task"]));
    [Theory]
    [InlineData(0, 1)]
    [InlineData(11, 1)]
    [InlineData(1, 10001)]
    [InlineData(1, 0)]
    public async Task Generation_BoundsInputBeforeInference(int count, int size)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator().GenerateAsync(Enumerable.Repeat(new string('x', size), count).ToArray()));
        await chat.DidNotReceive().GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Generation_PropagatesCancellation()
    {
        var generator = Generator();
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled<ChatResponse>(call.ArgAt<CancellationToken>(2)));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateAsync(["Task"], cts.Token));
    }
    [Fact]
    public async Task Improvement_PreservesIdentityAndWorkflowFields()
    {
        var story = new ProductBacklogItem { PbiId = 12, ProjectId = 9, Title = "Old", IsDraft = false, Origin = PbiOrigin.WebUserCreated, Status = PbiStatus.Done, AssignedToUserId = "owner" };
        var result = await Generator().ImproveAsync(story);
        Assert.Same(story, result); Assert.Equal(12, result.PbiId); Assert.Equal(9, result.ProjectId);
        Assert.Equal(PbiStatus.Done, result.Status); Assert.False(result.IsDraft);
        Assert.Equal("owner", result.AssignedToUserId); Assert.Equal(PbiOrigin.WebUserCreated, result.Origin);
    }
    [Fact]
    public async Task Improvement_RejectsMultipleItems() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Generator("[" + Valid[1..^1] + "," + Valid[1..^1] + "]")
            .ImproveAsync(new() { Title = "Task" }));
    [Fact]
    public async Task PbiPersistence_RemainsOutsideGenerationService()
    {
        var repo = Substitute.For<IPbiRepository>();
        var generator = Substitute.For<IStoryGenerationService>();
        var service = new PbiService(generator, repo);
        var item = new ProductBacklogItem { Title = "Manual" };
        repo.AddAsync(item, default).Returns(item);
        Assert.Same(item, await service.CreateDraftPbiAsync(item)); Assert.True(item.IsDraft);
        await repo.Received(1).AddAsync(item, default);
        await generator.DidNotReceiveWithAnyArgs().GenerateAsync(default!, default);
    }
}
