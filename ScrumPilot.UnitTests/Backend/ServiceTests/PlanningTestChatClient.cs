using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

/// <summary>Adapts the suites' in-memory response handlers to the host chat boundary. Never used by production.</summary>
internal sealed class PlanningTestChatClient(HttpClient http) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("http://127.0.0.1:11434/api/generate",
            new { prompt = string.Join("\n", messages.Select(message => message.Text)), model = options?.ModelId }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, document.RootElement.GetProperty("response").GetString()));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
