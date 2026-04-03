using Microsoft.Extensions.Configuration;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ScrumPilot.API.Services;
using ScrumPilot.Data.Repositories;
using ScrumPilot.Shared.Models;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ScrumPilot.UnitTests.Backend.ServiceTests
{
    public class StoryServiceTests : IDisposable
    {
        private readonly TestHttpMessageHandler _testHandler;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _mockConfiguration;
        private readonly IStoryRepository _mockRepository;
        private readonly StoryService _storyService;
        private HttpRequestMessage? _capturedRequest;

        public StoryServiceTests()
        {
            // Default handler that returns 200 OK
            _testHandler = new TestHttpMessageHandler((request, cancellation) =>
            {
                _capturedRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"response\": \"{}\"}", Encoding.UTF8, "application/json")
                });
            });

            _httpClient = new HttpClient(_testHandler);
            _mockConfiguration = Substitute.For<IConfiguration>();
            _mockRepository = Substitute.For<IStoryRepository>();
            _storyService = new StoryService(_httpClient, _mockConfiguration, _mockRepository);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _testHandler?.Dispose();
        }

        [Fact]
        public async Task GetAllStoriesAsync_ReturnsStoriesFromRepository()
        {
            // Arrange
            var expectedStories = new List<Story>
            {
                new Story { Id = 1, Title = "Story 1", Description = "Description 1" },
                new Story { Id = 2, Title = "Story 2", Description = "Description 2" }
            };
            _mockRepository.GetAllStoriesAsync().Returns(expectedStories);

            // Act
            var result = await _storyService.GetAllStoriesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedStories.Count, result.Count());
            Assert.Equal(expectedStories, result);
            await _mockRepository.Received(1).GetAllStoriesAsync();
        }

        [Fact]
        public async Task GetAllStoriesAsync_ReturnsEmptyList_WhenNoStoriesExist()
        {
            // Arrange
            var expectedStories = new List<Story>();
            _mockRepository.GetAllStoriesAsync().Returns(expectedStories);

            // Act
            var result = await _storyService.GetAllStoriesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            await _mockRepository.Received(1).GetAllStoriesAsync();
        }

        [Fact]
        public async Task GetDraftStoriesAsync_ReturnsDraftStoriesFromRepository()
        {
            // Arrange
            var expectedDraftStories = new List<Story>
            {
                new Story { Id = 1, Title = "Draft Story", Description = "Draft Description", IsDraft = true }
            };
            _mockRepository.GetDraftStoriesAsync().Returns(expectedDraftStories);

            // Act
            var result = await _storyService.GetDraftStoriesAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.True(result.First().IsDraft);
            await _mockRepository.Received(1).GetDraftStoriesAsync();
        }

        [Fact]
        public async Task CreateStoryAsync_CallsRepositoryAddAsync()
        {
            // Arrange
            var inputStory = new Story { Title = "New Story", Description = "New Description" };
            var createdStory = new Story { Id = 1, Title = inputStory.Title, Description = inputStory.Description };
            _mockRepository.AddAsync(inputStory).Returns(createdStory);

            // Act
            var result = await _storyService.CreateStoryAsync(inputStory);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(createdStory.Id, result.Id);
            Assert.Equal(createdStory.Title, result.Title);
            await _mockRepository.Received(1).AddAsync(inputStory);
        }

        [Fact]
        public async Task UpdateStoryAsync_CallsRepositoryUpdateAsync()
        {
            // Arrange
            var updatedStory = new Story { Id = 1, Title = "Updated Story", Description = "Updated Description" };
            _mockRepository.UpdateAsync(updatedStory).Returns(updatedStory);

            // Act
            var result = await _storyService.UpdateStoryAsync(updatedStory);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(updatedStory.Id, result.Id);
            Assert.Equal(updatedStory.Title, result.Title);
            await _mockRepository.Received(1).UpdateAsync(updatedStory);
        }

        [Fact]
        public async Task DeleteStoryAsync_ReturnsTrueWhenSuccessfullyDeleted()
        {
            // Arrange
            var storyId = 1;
            _mockRepository.DeleteAsync(storyId).Returns(true);

            // Act
            var result = await _storyService.DeleteStoryAsync(storyId);

            // Assert
            Assert.True(result);
            await _mockRepository.Received(1).DeleteAsync(storyId);
        }

        [Fact]
        public async Task DeleteStoryAsync_ReturnsFalseWhenStoryNotFound()
        {
            // Arrange
            var storyId = 999;
            _mockRepository.DeleteAsync(storyId).Returns(false);

            // Act
            var result = await _storyService.DeleteStoryAsync(storyId);

            // Assert
            Assert.False(result);
            await _mockRepository.Received(1).DeleteAsync(storyId);
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsStory_WhenValidResponse()
        {
            // Arrange
            var problemStatement = "As a user, I want to login";
            var aiResponse = new AiStoryResponse
            {
                Title = "User Login Feature",
                UserStory = "As a user, I want to login to the system, so that I can access my account.",
                AcceptanceCriteria = ["I see a login form", "I can enter credentials", "I am redirected after login"]
            };

            var ollamaResponse = new
            {
                response = JsonSerializer.Serialize(aiResponse)
            };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act
            var results = await service.GenerateAiStory(new List<string> { problemStatement });
            var result = results[0];

            // Assert
            Assert.NotNull(result);
            Assert.Equal(aiResponse.Title, result.Title);
            Assert.Contains(aiResponse.UserStory, result.Description);
            Assert.Contains("Acceptance Criteria:", result.Description);
            Assert.All(aiResponse.AcceptanceCriteria, criteria => 
                Assert.Contains(criteria, result.Description));
            Assert.Equal(StoryStatus.ToDo, result.Status);
            Assert.Equal(StoryPriority.Low, result.Priority);
            Assert.Equal(StoryOrigin.AiGenerated, result.Origin);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenOllamaBaseUrlNotConfigured()
        {
            // Arrange
            var problemStatement = "Test problem";
            _mockConfiguration["OllamaBaseUrl"].Returns((string?)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _storyService.GenerateAiStory(new List<string> { problemStatement }));
            Assert.Equal("OllamaBaseUrl is not configured.", exception.Message);
        }

        [Theory]
        [InlineData("")]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenOllamaBaseUrlEmpty(string baseUrl)
        {
            // Arrange
            var problemStatement = "Test problem";
            _mockConfiguration["OllamaBaseUrl"].Returns(baseUrl);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _storyService.GenerateAiStory(new List<string> { problemStatement }));
            Assert.Equal("OllamaBaseUrl is not configured.", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenOllamaBaseUrlIsWhitespace()
        {
            // Arrange
            var problemStatement = "Test problem";
            var baseUrl = "   "; // Just whitespace
            _mockConfiguration["OllamaBaseUrl"].Returns(baseUrl);

            // This will not trigger the validation check since the service only uses IsNullOrEmpty
            // So it will try to make HTTP call and fail differently
            var service = CreateServiceWithTimeout(); // Setup timeout to simulate connection failure

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateAiStory(new List<string> { problemStatement }));
            Assert.Contains("An unexpected error occurred while calling the Ollama API", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsHttpRequestException_WhenApiReturnsError()
        {
            // Arrange
            var problemStatement = "Test problem";
            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse("Error occurred", HttpStatusCode.InternalServerError);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => service.GenerateAiStory(new List<string> { problemStatement }));
            Assert.Contains("Ollama API request failed with status InternalServerError", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsTimeoutException_WhenRequestTimesOut()
        {
            // Arrange
            var problemStatement = "Test problem";
            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithTimeout();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => service.GenerateAiStory(new List<string> { problemStatement }));
            Assert.Equal("The request to Ollama timed out", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenResponseIsEmpty()
        {
            // Arrange
            var problemStatement = "Test problem";
            var ollamaResponse = new { response = "" };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateAiStory(new List<string> { problemStatement }));
            Assert.Contains("An unexpected error occurred while parsing the AI story response", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenResponseIsNotValidJson()
        {
            // Arrange
            var problemStatement = "Test problem";
            var ollamaResponse = new { response = "This is not JSON" };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateAiStory(new List<string> { problemStatement }));
            Assert.Contains("Failed to find a JSON object in the AI response", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_HandlesJsonWithExtraText_ExtractsValidJson()
        {
            // Arrange
            var problemStatement = "Test problem";
            var aiResponse = new AiStoryResponse
            {
                Title = "Test Story",
                UserStory = "As a user, I want to test, so that I can verify functionality.",
                AcceptanceCriteria = ["I see the test", "I can run the test"]
            };

            var responseWithExtraText = $"Here is your story: {JsonSerializer.Serialize(aiResponse)} Hope this helps!";
            var ollamaResponse = new { response = responseWithExtraText };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act
            var results = await service.GenerateAiStory(new List<string> { problemStatement });
            var result = results[0];

            // Assert
            Assert.NotNull(result);
            Assert.Equal(aiResponse.Title, result.Title);
            Assert.Contains(aiResponse.UserStory, result.Description);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenJsonHasInvalidSchema()
        {
            // Arrange
            var problemStatement = "Test problem";
            var invalidResponse = new { title = "Test", description = "Missing required fields" };
            var ollamaResponse = new { response = JsonSerializer.Serialize(invalidResponse) };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateAiStory(new List<string> { problemStatement }));
            Assert.Contains("Failed to parse AI response as JSON", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_SendsCorrectRequestToOllamaApi()
        {
            // Arrange
            var problemStatement = "As a user, I want to test the system";
            var baseUrl = "http://localhost:11434/";
            var model = "llama2";

            var aiResponse = new AiStoryResponse
            {
                Title = "Test Story",
                UserStory = "Test user story",
                AcceptanceCriteria = ["Test criteria"]
            };

            var ollamaResponse = new { response = JsonSerializer.Serialize(aiResponse) };

            SetupConfiguration(baseUrl, model);
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act
            await service.GenerateAiStory(new List<string> { problemStatement });

            // Assert - Verify the request was made correctly
            Assert.NotNull(_capturedRequest);
            var expectedUrl = $"{baseUrl}api/generate";
            Assert.Equal(expectedUrl, _capturedRequest.RequestUri?.ToString());
            Assert.Equal(HttpMethod.Post, _capturedRequest.Method);

            var requestContent = await _capturedRequest.Content!.ReadAsStringAsync();
            var requestObject = JsonSerializer.Deserialize<JsonElement>(requestContent);

            Assert.Equal(model, requestObject.GetProperty("model").GetString());
            Assert.Contains(problemStatement, requestObject.GetProperty("prompt").GetString());
            Assert.False(requestObject.GetProperty("stream").GetBoolean());
        }

        [Fact]
        public async Task GenerateAiStory_ReturnsCorrectCountAndProperties_WhenMultipleStatements()
        {
            // Arrange
            var aiResponse = new AiStoryResponse
            {
                Title = "Test Story",
                UserStory = "As a user, I want to test, so that I can verify functionality.",
                AcceptanceCriteria = ["I see the result"]
            };

            var ollamaResponse = new { response = JsonSerializer.Serialize(aiResponse) };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act
            var result = await service.GenerateAiStory(["Statement 1", "Statement 2", "Statement 3"]);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.All(result, story =>
            {
                Assert.Equal(StoryOrigin.AiGenerated, story.Origin);
                Assert.Equal(StoryStatus.ToDo, story.Status);
            });
        }

        [Fact]
        public async Task GenerateAiStory_PropagatesException_WhenAnyStoryGenerationFails()
        {
            // Arrange
            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse("Error body", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(
                () => service.GenerateAiStory(["Problem 1", "Problem 2"]));
        }

        private void SetupConfiguration(string baseUrl, string model)
        {
            _mockConfiguration["OllamaBaseUrl"].Returns(baseUrl);
            _mockConfiguration["OllamaModel"].Returns(model);
        }

        private void ConfigureHttpResponse(string responseContent, HttpStatusCode statusCode)
        {
            // Replace the handler's response function
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };

            // Create new handler with the configured response
            var newHandler = new TestHttpMessageHandler((request, cancellation) =>
            {
                _capturedRequest = request;
                return Task.FromResult(response);
            });

            // Replace the HttpClient's handler (requires recreating the service)
            _httpClient.Dispose();
            var newHttpClient = new HttpClient(newHandler);

            // We need to recreate the service with the new client
            var newService = new StoryService(newHttpClient, _mockConfiguration, _mockRepository);

            // Update the field reference (this is a limitation of this approach)
            // For a production test, consider using dependency injection or factory pattern
        }

        private void ConfigureHttpTimeout()
        {
            var newHandler = new TestHttpMessageHandler((request, cancellation) =>
            {
                _capturedRequest = request;
                throw new TaskCanceledException("The operation was canceled.");
            });

            _httpClient.Dispose();
            var newHttpClient = new HttpClient(newHandler);
        }

        private StoryService CreateServiceWithResponse(string responseContent, HttpStatusCode statusCode)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };

            var handler = new TestHttpMessageHandler((request, cancellation) =>
            {
                _capturedRequest = request;
                return Task.FromResult(response);
            });

            var httpClient = new HttpClient(handler);
            return new StoryService(httpClient, _mockConfiguration, _mockRepository);
        }

        private StoryService CreateServiceWithTimeout()
        {
            var handler = new TestHttpMessageHandler((request, cancellation) =>
            {
                _capturedRequest = request;
                throw new TaskCanceledException("The operation was canceled.");
            });

            var httpClient = new HttpClient(handler);
            return new StoryService(httpClient, _mockConfiguration, _mockRepository);
        }
    }
}