using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using System.Text;
using System.Text.Json;

namespace ScrumPilot.API.Services
{
    public class PbiService : IPbiService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IPbiRepository _pbiRepository;
        public PbiService(HttpClient httpClient, IConfiguration configuration, IPbiRepository pbiRepository)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _pbiRepository = pbiRepository;
        }

        public async Task<IEnumerable<ProductBacklogItem>> GetAllPbisAsync()
        {
            return await _pbiRepository.GetAllPbisAsync();
        }

        //public async Task<IEnumerable<ProductBacklogItem>> GetActivePbisAsync(int epicId) //This is for the Discord bot - Future State
        //{
        //    return await _pbiRepository.GetActivePbisAsync(epicId);
        //}

        public async Task<IEnumerable<ProductBacklogItem>> GetNonDraftPbisAsync()
        {
            return await _pbiRepository.GetNonDraftPbisAsync();
        }

        public async Task<IEnumerable<ProductBacklogItem>> GetDraftPbisAsync()
        {
            return await _pbiRepository.GetDraftPbisAsync();
        }

        public async Task<IEnumerable<ProductBacklogItem>> GetFilteredPbisAsync(int? sprintId, int? epicId, int? projectId = null)
        {
            return await _pbiRepository.GetFilteredPbisAsync(sprintId, epicId, projectId);
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
        private async Task<ProductBacklogItem> GenerateAiPbi(string problemStatement)
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

        public async Task<ProductBacklogItem> ImprovePbiAsync(ProductBacklogItem pbi)
        {
            var groqApiKey = _configuration["GroqApiKey"];
            var prompt = BuildImprovementPrompt(pbi);
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

            var aiPbiResponse = ParseAiStoryResponse(responseContent);

            pbi.Title = aiPbiResponse.Title;
            pbi.Description = $"{aiPbiResponse.UserStory}\n\nAcceptance Criteria:\n{string.Join("\n", aiPbiResponse.AcceptanceCriteria.Select(ac => $"\u2022 {ac}"))}";

            // Return without saving — caller decides whether to commit
            return pbi;
        }

        public async Task<List<ProductBacklogItem>> GenerateAiPbis(List<string> problemStatements)
        {
            var pbis = new List<ProductBacklogItem>();

            foreach (var problemStatement in problemStatements)
            {
                var pbi = await GenerateAiPbi(problemStatement);
                pbis.Add(pbi);
            }

            return pbis;
        }

        private string BuildPrompt(string problemStatement)
        {
            return $@"You are helping generate Scrum user stories.

                    Generate a Scrum user story for the following problem statement and return it as a JSON object.

                    The JSON must have exactly these three keys:
                    - title: a short, specific title describing the feature or need
                    - userStory: written as 'As a [specific role], I want [specific goal], so that [specific benefit].'
                    - acceptanceCriteria: an array of 3 to 5 strings, each beginning with 'I see' and describing a concrete, observable outcome

                    Do not copy these instructions into the output. Do not use placeholder text. Return only the JSON object with no markdown, no explanation, and no extra keys.

                    Problem statement:
                    {problemStatement}";
        }

        private string BuildImprovementPrompt(ProductBacklogItem pbi)
        {
            return $@"You are helping improve a Scrum Product Backlog Item.

                    Rewrite and improve the following PBI and return it as a JSON object.

                    The JSON must have exactly these three keys:
                    - title: a short, specific title describing the feature or need
                    - userStory: written as 'As a [specific role], I want [specific goal], so that [specific benefit].'
                    - acceptanceCriteria: an array of 3 to 5 strings, each beginning with 'I see' and describing a concrete, observable outcome

                    Do not copy these instructions into the output. Do not use placeholder text. Return only the JSON object with no markdown, no explanation, and no extra keys.

                    Current PBI:
                    Title: {pbi.Title}
                    Description: {pbi.Description}";
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
                throw new InvalidOperationException($"An unexpected error occurred while parsing the AI PBI response: {ex.Message}", ex);
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

        public async Task<ProductBacklogItem> CreatePbiAsync(ProductBacklogItem story)
        {
            story.IsDraft = false;
            return await _pbiRepository.AddAsync(story);
        }

        public async Task<ProductBacklogItem> CreateDraftPbiAsync(ProductBacklogItem story)
        {
            story.IsDraft = true;
            return await _pbiRepository.AddAsync(story);
        }

        public async Task<ProductBacklogItem> CommitDraftPbiAsync(ProductBacklogItem draftPbi)
        {
            var existingDraft = await _pbiRepository.GetByIdAsync(draftPbi.PbiId);
            if (existingDraft is null || !existingDraft.IsDraft)
            {
                throw new KeyNotFoundException("Draft PBI not found.");
            }

            existingDraft.IsDraft = false;
            existingDraft.LastUpdated = DateTime.UtcNow;

            return await _pbiRepository.UpdateAsync(existingDraft);
        }

        public async Task<ProductBacklogItem> UpdatePbiAsync(ProductBacklogItem pbi)
        {
            pbi.LastUpdated = DateTime.UtcNow;
            return await _pbiRepository.UpdateAsync(pbi);
        }

        public async Task<bool> DeletePbiAsync(int id)
        {
            return await _pbiRepository.DeleteAsync(id);
        }

        public async Task<ProductBacklogItem> CommitPbiAsync(ProductBacklogItem pbi)
        {
            pbi.IsDraft = false;
            pbi.LastUpdated = DateTime.UtcNow;
            return await _pbiRepository.UpdateAsync(pbi);
        }
    }
}
