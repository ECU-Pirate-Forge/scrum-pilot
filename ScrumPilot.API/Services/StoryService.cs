using AutoFixture;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using ScrumPilot.Data.Repositories;
using System.Text;
using System.Text.Json;

namespace ScrumPilot.API.Services
{
    public class StoryService : IStoryService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IStoryRepository _storyRepository;

        public StoryService(HttpClient httpClient, IConfiguration configuration, IStoryRepository storyRepository)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _storyRepository = storyRepository;
        }

        public async Task<IEnumerable<Story>> GetAllStoriesAsync()
        {
            return await _storyRepository.GetAllStoriesAsync();
        }

        public async Task<IEnumerable<Story>> GetDraftStoriesAsync()
        {
            return await _storyRepository.GetDraftStoriesAsync();
        }

        /// <summary>
        /// Generates a new AI-based Scrum user story from a given problem statement using the configured Ollama API.
        /// </summary>
        /// <param name="problemStatement">The problem statement to generate the user story from.</param>
        /// <returns>
        /// A <see cref="Story"/> object containing the generated user story, acceptance criteria, and metadata.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the Ollama base URL is not configured or if the AI response cannot be parsed.
        /// </exception>
        /// <exception cref="HttpRequestException">
        /// Thrown if the Ollama API request fails.
        /// </exception>
        /// <exception cref="TimeoutException">
        /// Thrown if the request to the Ollama API times out.
        /// </exception>
        private async Task<Story> GenerateAiStory(string problemStatement)
        {
            var ollamaBaseUrl = _configuration["OllamaBaseUrl"];
            var ollamaModel = _configuration["OllamaModel"];

            if (string.IsNullOrEmpty(ollamaBaseUrl))
            {
                throw new InvalidOperationException("OllamaBaseUrl is not configured.");
            }

            var prompt = BuildPrompt(problemStatement);
            var responseContent = await CallOllamaApiAsync(ollamaBaseUrl, ollamaModel, prompt);
            var aiStoryResponse = ParseAiStoryResponse(responseContent);

            var story = new Story
            {
                Title = aiStoryResponse.Title,
                Description = $"{aiStoryResponse.UserStory}\n\nAcceptance Criteria:\n{string.Join("\n", aiStoryResponse.AcceptanceCriteria.Select(ac => $"• {ac}"))}",
                Status = StoryStatus.ToDo,
                Priority = StoryPriority.Low,
                Origin = StoryOrigin.AiGenerated,
                IsDraft = true,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            return story;
        }

        public async Task<List<Story>> GenerateAiStory(List<string> problemStatements)
        {
            var stories = new List<Story>();

            foreach (var problemStatement in problemStatements)
            {
                var story = await GenerateAiStory(problemStatement);
                stories.Add(story);
            }

            return stories;
        }

        private string BuildPrompt(string problemStatement)
        {
            return $@"You are helping generate Scrum user stories.

                    Return ONLY valid JSON matching this schema:
                    {{
                    ""title"": ""string"",
                    ""userStory"": ""string"",
                    ""acceptanceCriteria"": [""string"", ""string"", ""string""]
                    }}

                    Rules:
                    - userStory must be in the format: ""As a <role>, I want <goal>, so that <benefit>.""
                    - acceptanceCriteria in bulleted list in the format: ""I see""
                    - No extra keys. No markdown. JSON only.

                    Problem statement:
                    {problemStatement}";
        }

        private async Task<string> CallOllamaApiAsync(string baseUrl, string model, string prompt)
        {
            var requestBody = new
            {
                model = model,
                prompt = prompt,
                stream = false,
                format = "json"
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{baseUrl}api/generate", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Ollama API request failed with status {response.StatusCode}: {errorContent}");
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("The request to Ollama timed out", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An unexpected error occurred while calling the Ollama API: {ex.Message}", ex);
            }
        }

        private AiStoryResponse ParseAiStoryResponse(string responseContent)
        {
            try
            {
                var ollamaResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var aiResponseText = ollamaResponse.GetProperty("response").GetString();

                if (string.IsNullOrEmpty(aiResponseText))
                {
                    throw new InvalidOperationException("Ollama returned an empty response");
                }

                // Extract the first valid JSON object from the response string
                string? jsonObject = ExtractFirstJsonObject(aiResponseText);
                if (string.IsNullOrEmpty(jsonObject))
                {
                    throw new InvalidOperationException($"Failed to find a JSON object in the AI response. Response: {aiResponseText}");
                }

                AiStoryResponse? aiStoryResponse = null;
                try
                {
                    aiStoryResponse = JsonSerializer.Deserialize<AiStoryResponse>(jsonObject, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Failed to parse AI response as JSON. Response: {jsonObject}", ex);
                }

                if (aiStoryResponse == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize AI response. Response: {jsonObject}");
                }

                return aiStoryResponse;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An unexpected error occurred while parsing the AI story response: {ex.Message}", ex);
            }
        }

        // Helper to extract the first JSON object from a string
        //The models sometimes return extra text befor the JSON despite the instructions not to. 
        //This is to avoid errors as a result. 
        private static string? ExtractFirstJsonObject(string input)
        {
            int firstBrace = input.IndexOf('{');
            if (firstBrace == -1) return null;
            int depth = 0;
            for (int i = firstBrace; i < input.Length; i++)
            {
                if (input[i] == '{') depth++;
                else if (input[i] == '}') depth--;
                if (depth == 0)
                {
                    return input.Substring(firstBrace, i - firstBrace + 1);
                }
            }
            return null;
        }

        public async Task<Story> CreateStoryAsync(Story story)
        {
            story.IsDraft = false;
            return await _storyRepository.AddAsync(story);
        }

        public async Task<Story> CreateDraftStoryAsync(Story story)
        {
            story.IsDraft = true;
            return await _storyRepository.AddAsync(story);
        }

        public async Task<Story> UpdateStoryAsync(Story story)
        {
            return await _storyRepository.UpdateAsync(story);
        }

        public async Task<bool> DeleteStoryAsync(int id)
        {
            return await _storyRepository.DeleteAsync(id);
        }

    }
}
