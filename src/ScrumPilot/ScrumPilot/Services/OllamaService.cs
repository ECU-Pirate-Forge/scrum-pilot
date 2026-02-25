using System.Text;
using System.Text.Json;

namespace ScrumPilot.Services;

public class OllamaResponse
{
    public string model { get; set; } = "";
    public string response { get; set; } = "";
    public bool done { get; set; }
}

public class OllamaService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public OllamaService(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
    }

    public async Task<string> GenerateUserStoryAsync(
        string title,
        string description,
        string model = "phi3")
    {
        var prompt = $"""
            You are an expert Agile product owner. 
            Generate a well-structured user story based on:

            Title: {title}
            Description: {description}

            Format your response EXACTLY like this:

            ## User Story
            **As a** [type of user], **I want** [goal], **so that** [benefit].

            ## Acceptance Criteria
            - [ ] Criterion 1
            - [ ] Criterion 2
            - [ ] Criterion 3

            ## Story Points
            Estimated: [1/2/3/5/8/13]

            ## Notes
            [Any additional context or technical notes]
            """;

        var payload = new
        {
            model = model,
            prompt = prompt,
            stream = false
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync($"{_baseUrl}/api/generate", content);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseBody);

        return ollamaResponse?.response ?? "No response received.";
    }
}
