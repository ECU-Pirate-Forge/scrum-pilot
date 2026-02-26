using Microsoft.Extensions.Configuration;
using ScrumPilot.API.Services;
using ScrumPilot.Shared.Models;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ScrumPilot.UnitTests.Backend.Service_Tests
{
    public class StoryServiceTests : IDisposable
    {
        private readonly Mock<HttpMessageHandler> _mockHandler;
        private readonly HttpClient _mockHttpClient;
        private readonly IConfiguration _mockConfiguration;
        private readonly StoryService _storyService;

        public StoryServiceTests()
        {
            _mockHandler = new Mock<HttpMessageHandler>();
            _mockHttpClient = new HttpClient(_mockHandler.Object);
            _mockConfiguration = Substitute.For<IConfiguration>();
            _storyService = new StoryService(_mockHttpClient, _mockConfiguration);
        }

        public void Dispose()
        {
            _mockHttpClient?.Dispose();
        }

        #region GetStories Tests

        [Fact]
        public void GetStories_ReturnsListOfStories_WithExpectedCount()
        {
            // Act
            var result = _storyService.GetStories();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(5, result.Count); // AutoFixture generates 5 stories
            Assert.All(result, story =>
            {
                Assert.NotEqual(Guid.Empty, story.Id);
                Assert.NotNull(story.Title);
                Assert.NotNull(story.Description);
                Assert.True(Enum.IsDefined(typeof(StoryStatus), story.Status));
                Assert.True(Enum.IsDefined(typeof(StoryPriority), story.Priority));
            });
        }

        [Fact]
        public void GetStories_ReturnsUniqueStories_OnMultipleCalls()
        {
            // Act
            var firstCall = _storyService.GetStories();
            var secondCall = _storyService.GetStories();

            // Assert
            Assert.NotEqual(firstCall.Select(s => s.Id), secondCall.Select(s => s.Id));
        }

        #endregion

        #region GenerateAiStory Tests

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
            SetupHttpResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act
            var result = await _storyService.GenerateAiStory(problemStatement);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(aiResponse.Title, result.Title);
            Assert.Contains(aiResponse.UserStory, result.Description);
            Assert.Contains("Acceptance Criteria:", result.Description);
            Assert.All(aiResponse.AcceptanceCriteria, criteria => 
                Assert.Contains(criteria, result.Description));
            Assert.Equal(StoryStatus.ToDo, result.Status);
            Assert.Equal(StoryPriority.Low, result.Priority);
            Assert.True(result.IsAiGenerated);
            Assert.NotEqual(Guid.Empty, result.Id);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenOllamaBaseUrlNotConfigured()
        {
            // Arrange
            var problemStatement = "Test problem";
            _mockConfiguration["OllamaBaseUrl"].Returns((string?)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _storyService.GenerateAiStory(problemStatement));
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
                () => _storyService.GenerateAiStory(problemStatement));
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
            SetupHttpTimeout(); // Setup timeout to simulate connection failure

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _storyService.GenerateAiStory(problemStatement));
            Assert.Contains("An unexpected error occurred while calling the Ollama API", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsHttpRequestException_WhenApiReturnsError()
        {
            // Arrange
            var problemStatement = "Test problem";
            SetupConfiguration("http://localhost:11434/", "llama2");
            SetupHttpResponse("Error occurred", HttpStatusCode.InternalServerError);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => _storyService.GenerateAiStory(problemStatement));
            Assert.Contains("Ollama API request failed with status InternalServerError", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsTimeoutException_WhenRequestTimesOut()
        {
            // Arrange
            var problemStatement = "Test problem";
            SetupConfiguration("http://localhost:11434/", "llama2");
            SetupHttpTimeout();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => _storyService.GenerateAiStory(problemStatement));
            Assert.Equal("The request to Ollama timed out", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenResponseIsEmpty()
        {
            // Arrange
            var problemStatement = "Test problem";
            var ollamaResponse = new { response = "" };
            
            SetupConfiguration("http://localhost:11434/", "llama2");
            SetupHttpResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _storyService.GenerateAiStory(problemStatement));
            Assert.Contains("An unexpected error occurred while parsing the AI story response", exception.Message);
        }

        [Fact]
        public async Task GenerateAiStory_ThrowsInvalidOperationException_WhenResponseIsNotValidJson()
        {
            // Arrange
            var problemStatement = "Test problem";
            var ollamaResponse = new { response = "This is not JSON" };
            
            SetupConfiguration("http://localhost:11434/", "llama2");
            SetupHttpResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _storyService.GenerateAiStory(problemStatement));
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
            SetupHttpResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act
            var result = await _storyService.GenerateAiStory(problemStatement);

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
            SetupHttpResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _storyService.GenerateAiStory(problemStatement));
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
            
            HttpRequestMessage? capturedRequest = null;
            SetupHttpResponseWithRequestCapture(
                JsonSerializer.Serialize(ollamaResponse), 
                HttpStatusCode.OK,
                req => capturedRequest = req);

            // Act
            await _storyService.GenerateAiStory(problemStatement);

            // Assert - Verify the request was made correctly
            Assert.NotNull(capturedRequest);
            var expectedUrl = $"{baseUrl}api/generate";
            Assert.Equal(expectedUrl, capturedRequest.RequestUri?.ToString());
            Assert.Equal(HttpMethod.Post, capturedRequest.Method);
            
            var requestContent = await capturedRequest.Content!.ReadAsStringAsync();
            var requestObject = JsonSerializer.Deserialize<JsonElement>(requestContent);
            
            Assert.Equal(model, requestObject.GetProperty("model").GetString());
            Assert.Contains(problemStatement, requestObject.GetProperty("prompt").GetString());
            Assert.False(requestObject.GetProperty("stream").GetBoolean());
        }

        #endregion

        #region Helper Methods

        private void SetupConfiguration(string baseUrl, string model)
        {
            _mockConfiguration["OllamaBaseUrl"].Returns(baseUrl);
            _mockConfiguration["OllamaModel"].Returns(model);
        }

        private void SetupHttpResponse(string responseContent, HttpStatusCode statusCode)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };

            _mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", 
                    ItExpr.IsAny<HttpRequestMessage>(), 
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);
        }

        private void SetupHttpTimeout()
        {
            _mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new TaskCanceledException("The operation was canceled."));
        }

        private void SetupHttpResponseWithRequestCapture(string responseContent, HttpStatusCode statusCode, Action<HttpRequestMessage> captureCallback)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "application/json")
            };

            _mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => captureCallback(req))
                .ReturnsAsync(response);
        }

        #endregion
    }
}