using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
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

        public async Task<IEnumerable<ProductBacklogItem>> GetAllStoriesAsync()
        {
            return await _storyRepository.GetAllStoriesAsync();
        }

        //public async Task<IEnumerable<Story>> GetActiveStoriesAsync(int epicId)
        //{
        //    return await _storyRepository.GetActiveStoriesAsync(epicId);
        //}

        public async Task<IEnumerable<ProductBacklogItem>> GetNonDraftStoriesAsync()
        {
            return await _storyRepository.GetNonDraftStoriesAsync();
        }

        public async Task<IEnumerable<ProductBacklogItem>> GetDraftStoriesAsync()
        {
            return await _storyRepository.GetDraftStoriesAsync();
        }

        /// <summary>
        /// Generates a new AI-based Scrum user story from a given problem statement using the configured Ollama API.
        /// </summary>
        /// <param name="problemStatement">The problem statement to generate the user story from.</param>
        /// <returns>
        /// A <see cref="ProductBacklogItem"/> object containing the generated user story, acceptance criteria, and metadata.
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
        private async Task<ProductBacklogItem> GenerateAiStory(string problemStatement)
        {
            var groqApiKey = _configuration["GroqApiKey"];
            var prompt = BuildPrompt(problemStatement);
            string responseContent;

            if (!string.IsNullOrEmpty(groqApiKey))
            {
                // Use Groq when API key is configured (e.g. Render production)
                var groqModel = _configuration["GroqModel"] ?? "llama-3.3-70b-versatile";
                responseContent = await CallGroqApiAsync(groqApiKey, groqModel, prompt);
            }
            else
            {
                // Fall back to local Ollama (development)
                var ollamaBaseUrl = _configuration["OllamaBaseUrl"];
                var ollamaModel = _configuration["OllamaModel"];

                if (string.IsNullOrEmpty(ollamaBaseUrl))
                    throw new InvalidOperationException("No AI provider configured. Set GeminiApiKey or OllamaBaseUrl.");

                responseContent = await CallOllamaApiAsync(ollamaBaseUrl, ollamaModel, prompt);
            }

            var aiStoryResponse = ParseAiStoryResponse(responseContent);

            var story = new ProductBacklogItem
            {
                Title = aiStoryResponse.Title,
                Description = $"{aiStoryResponse.UserStory}\n\nAcceptance Criteria:\n{string.Join("\n", aiStoryResponse.AcceptanceCriteria.Select(ac => $"• {ac}"))}",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Low,
                Origin = PbiOrigin.AiGenerated,
                IsDraft = true,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            return story;
        }

        public async Task<List<ProductBacklogItem>> GenerateAiStories(List<string> problemStatements)
        {
            var stories = new List<ProductBacklogItem>();

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

        private async Task<string> CallGroqApiAsync(string apiKey, string model, string prompt)
        {
            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = content;
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Groq API request failed with status {response.StatusCode}: {errorContent}");
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException("The request to Groq timed out", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An unexpected error occurred while calling the Groq API: {ex.Message}", ex);
            }
        }

        private AiStoryResponse ParseAiStoryResponse(string responseContent)
        {
            try
            {
                var parsedResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                string? aiResponseText = null;
                // OpenAI/OpenRouter format: choices[0].message.content
                if (parsedResponse.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    aiResponseText = choices[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                }
                // Native Gemini format: candidates[0].content.parts[0].text
                else if (parsedResponse.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0)
                {
                    aiResponseText = candidates[0]
                        .GetProperty("content")
                        .GetProperty("parts")[0]
                        .GetProperty("text")
                        .GetString();
                }
                else if (parsedResponse.TryGetProperty("response", out var ollamaResponse))
                {
                    // Ollama format: response
                    aiResponseText = ollamaResponse.GetString();
                }

                if (string.IsNullOrEmpty(aiResponseText))
                {
                    throw new InvalidOperationException("AI provider returned an empty response");
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

        public async Task<ProductBacklogItem> CreateStoryAsync(ProductBacklogItem story)
        {
            story.IsDraft = false;
            return await _storyRepository.AddAsync(story);
        }

        public async Task<ProductBacklogItem> CreateDraftStoryAsync(ProductBacklogItem story)
        {
            story.IsDraft = true;
            return await _storyRepository.AddAsync(story);
        }

        public async Task<ProductBacklogItem> CommitDraftStoryAsync(ProductBacklogItem draftStory)
        {
            var existingDraft = await _storyRepository.GetByIdAsync(draftStory.PbiId);
            if (existingDraft is null || !existingDraft.IsDraft)
            {
                throw new KeyNotFoundException("Draft story not found.");
            }

            existingDraft.IsDraft = false;
            existingDraft.LastUpdated = DateTime.UtcNow;

            return await _storyRepository.UpdateAsync(existingDraft);
        }

        public async Task<ProductBacklogItem> UpdateStoryAsync(ProductBacklogItem story)
        {
            story.LastUpdated = DateTime.UtcNow;
            return await _storyRepository.UpdateAsync(story);
        }

        public async Task<bool> DeleteStoryAsync(int id) //Currently a hard delete. Maybe we reconsider this?
        {
            return await _storyRepository.DeleteAsync(id);
        }

        public async Task<ProductBacklogItem> CommitStoryAsync(ProductBacklogItem story)
        {
            story.IsDraft = false;
            story.LastUpdated = DateTime.UtcNow;
            return await _storyRepository.UpdateAsync(story);
        }
    }
}
