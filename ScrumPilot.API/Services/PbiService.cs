using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        private async Task<List<ProductBacklogItem>> GenerateAiPbis(string problemStatement)
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

            return ParseAiStoryResponses(responseContent)
                .Select(CreateGeneratedPbi)
                .ToList();
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

            var aiPbiResponses = ParseAiStoryResponses(responseContent);
            if (aiPbiResponses.Count != 1)
            {
                throw new InvalidOperationException(
                    $"AI improvement must return exactly one PBI, but returned {aiPbiResponses.Count}.");
            }

            var aiPbiResponse = aiPbiResponses[0];
            pbi.Type = aiPbiResponse.Type!.Value;
            pbi.Priority = aiPbiResponse.Priority!.Value;
            pbi.StoryPoints = (PbiPoints)aiPbiResponse.StoryPoints!.Value;
            pbi.Title = aiPbiResponse.Title;
            pbi.Description = BuildDescription(aiPbiResponse);

            // Return without saving — caller decides whether to commit
            return pbi;
        }

        public async Task<List<ProductBacklogItem>> GenerateAiPbis(List<string> problemStatements)
        {
            var pbis = new List<ProductBacklogItem>();

            foreach (var problemStatement in problemStatements)
            {
                var generatedPbis = await GenerateAiPbis(problemStatement);
                pbis.AddRange(generatedPbis);
            }

            return pbis;
        }

        private string BuildPrompt(string problemStatement)
        {
            return $@"
You are an expert at breaking a problem statement down into independent, parallelizable tasks.

# Instructions

1. Read the problem statement carefully.
2. Identify the primary deliverable.
3. Extract all functional requirements.
4. Extract all constraints (time, tools, environment).
5. Identify implicit dependencies.
6. Produce a dependency graph.
7. Slice tasks into independent units that can be parallelized.
8. For each task, output:
   - type: one of 'Story', 'Bug', or 'Task'
   - priority: one of 'None', 'Low', 'Medium', or 'High'
   - storyPoints: one of 0, 1, 2, 3, 5, 8, 13, or 21
   - title: a short, specific title describing the feature or need
   - userStory: written as 'As a [specific role], I want [specific goal], so that [specific benefit].'
   - acceptanceCriteria: an array of 3 to 7 strings, each beginning with 'I see' and describing a concrete, observable outcome
9. Validate that tasks cover the entire problem statement.
10. Generate one or more Scrum product backlog items for the following problem statement and return them as a JSON array.
    - Each array item must have exactly these six keys:
      - type: one of 'Story', 'Bug', or 'Task'
      - priority: one of 'None', 'Low', 'Medium', or 'High'
      - storyPoints: one of 0, 1, 2, 3, 5, 8, 13, or 21
      - title: a short, specific title describing the feature or need
      - userStory: written as 'As a [specific role], I want [specific goal], so that [specific benefit].'
      - acceptanceCriteria: an array of 3 to 5 strings, each beginning with 'I see' and describing a concrete, observable outcome

Do not copy these instructions into the output. Do not use placeholder text. Return only the JSON array with no markdown, no explanation, and no extra keys.

Problem statement: {problemStatement}";
                    
        }

        private string BuildImprovementPrompt(ProductBacklogItem pbi)
        {
            return $@"You are helping improve a Scrum Product Backlog Item.

                    Rewrite and improve the following PBI and return a JSON array containing exactly one item.

                    The array item must have exactly these six keys:
                    - type: one of 'Story', 'Bug', or 'Task'
                    - priority: one of 'None', 'Low', 'Medium', or 'High'
                    - storyPoints: one of 0, 1, 2, 3, 5, 8, 13, or 21
                    - title: a short, specific title describing the feature or need
                    - userStory: written as 'As a [specific role], I want [specific goal], so that [specific benefit].'
                    - acceptanceCriteria: an array of 3 to 5 strings, each beginning with 'I see' and describing a concrete, observable outcome

                    Do not copy these instructions into the output. Do not use placeholder text. Return only the one-item JSON array with no markdown, no explanation, and no extra keys.

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
                format = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            type = new { type = "string", @enum = new[] { "Story", "Bug", "Task" } },
                            priority = new { type = "string", @enum = new[] { "None", "Low", "Medium", "High" } },
                            storyPoints = new { type = "integer", @enum = new[] { 0, 1, 2, 3, 5, 8, 13, 21 } },
                            title = new { type = "string" },
                            userStory = new { type = "string" },
                            acceptanceCriteria = new
                            {
                                type = "array",
                                items = new { type = "string" },
                                minItems = 1
                            }
                        },
                        required = new[]
                        {
                            "type",
                            "priority",
                            "storyPoints",
                            "title",
                            "userStory",
                            "acceptanceCriteria"
                        },
                        additionalProperties = false
                    }
                }
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

        private List<AiStoryResponse> ParseAiStoryResponses(string responseContent)
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

                var jsonArray = ExtractFirstJsonArray(aiResponseText);
                if (string.IsNullOrEmpty(jsonArray))
                {
                    throw new InvalidOperationException($"Failed to find a JSON array in the AI response. Response: {aiResponseText}");
                }

                List<AiStoryResponse>? aiStoryResponses;
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
                    aiStoryResponses = JsonSerializer.Deserialize<List<AiStoryResponse>>(jsonArray, options);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Failed to parse AI response as JSON. Response: {jsonArray}", ex);
                }

                if (aiStoryResponses == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize AI response. Response: {jsonArray}");
                }

                if (aiStoryResponses.Count == 0)
                {
                    throw new InvalidOperationException("AI provider returned an empty PBI array.");
                }

                foreach (var response in aiStoryResponses)
                {
                    ValidateAiStoryResponse(response);
                }

                return aiStoryResponses;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"An unexpected error occurred while parsing the AI PBI response: {ex.Message}", ex);
            }
        }

        private static string? ExtractFirstJsonArray(string input)
        {
            var firstBracket = input.IndexOf('[');
            if (firstBracket == -1) return null;

            int depth = 0;
            var inString = false;
            var isEscaped = false;

            for (var i = firstBracket; i < input.Length; i++)
            {
                var character = input[i];
                if (inString)
                {
                    if (isEscaped)
                    {
                        isEscaped = false;
                    }
                    else if (character == '\\')
                    {
                        isEscaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                }
                else if (character == '[')
                {
                    depth++;
                }
                else if (character == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return input.Substring(firstBracket, i - firstBracket + 1);
                    }
                }
            }

            return null;
        }

        private static void ValidateAiStoryResponse(AiStoryResponse response)
        {
            if (!response.Type.HasValue)
                throw new InvalidOperationException("AI PBI response is missing type.");
            if (!response.Priority.HasValue)
                throw new InvalidOperationException("AI PBI response is missing priority.");
            if (!response.StoryPoints.HasValue || !Enum.IsDefined(typeof(PbiPoints), response.StoryPoints.Value))
                throw new InvalidOperationException("AI PBI response has missing or invalid storyPoints.");
            if (string.IsNullOrWhiteSpace(response.Title))
                throw new InvalidOperationException("AI PBI response is missing title.");
            if (string.IsNullOrWhiteSpace(response.UserStory))
                throw new InvalidOperationException("AI PBI response is missing userStory.");
            if (response.AcceptanceCriteria == null ||
                response.AcceptanceCriteria.Count == 0 ||
                response.AcceptanceCriteria.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException("AI PBI response has missing or invalid acceptanceCriteria.");
            }
        }

        private static ProductBacklogItem CreateGeneratedPbi(AiStoryResponse response)
        {
            var now = DateTime.UtcNow;
            return new ProductBacklogItem
            {
                Type = response.Type!.Value,
                Priority = response.Priority!.Value,
                StoryPoints = (PbiPoints)response.StoryPoints!.Value,
                Title = response.Title!,
                Description = BuildDescription(response),
                Status = PbiStatus.ToDo,
                Origin = PbiOrigin.AiGenerated,
                IsDraft = true,
                DateCreated = now,
                LastUpdated = now
            };
        }

        private static string BuildDescription(AiStoryResponse response) =>
            $"{response.UserStory}\n\nAcceptance Criteria:\n{string.Join("\n", response.AcceptanceCriteria!.Select(ac => $"• {ac}"))}";

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
