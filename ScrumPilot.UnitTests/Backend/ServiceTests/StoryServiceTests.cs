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
        private readonly IPbiRepository _mockRepository;
        private readonly PbiService _pbiService;
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
            _mockRepository = Substitute.For<IPbiRepository>();
            _pbiService = new PbiService(_httpClient, _mockConfiguration, _mockRepository);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _testHandler?.Dispose();
        }

        [Fact]
        public async Task GetAllPbisAsync_ReturnsPbisFromRepository()
        {
            // Arrange
            var expectedStories = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "Story 1", Description = "Description 1" },
                new ProductBacklogItem { PbiId = 2, Title = "Story 2", Description = "Description 2" }
            };
            _mockRepository.GetAllPbisAsync().Returns(expectedStories);

            // Act
            var result = await _pbiService.GetAllPbisAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedStories.Count, result.Count());
            Assert.Equal(expectedStories, result);
            await _mockRepository.Received(1).GetAllPbisAsync();
        }

        [Fact]
        public async Task GetAllPbisAsync_ReturnsEmptyList_WhenNoPbisExist()
        {
            // Arrange
            var expectedPbis = new List<ProductBacklogItem>();
            _mockRepository.GetAllPbisAsync().Returns(expectedPbis);
            // Act
            var result = await _pbiService.GetAllPbisAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            await _mockRepository.Received(1).GetAllPbisAsync();
        }

        [Fact]
        public async Task GetDraftPbisAsync_ReturnsDraftPbisFromRepository()
        {
            // Arrange
            var expectedDraftStories = new List<ProductBacklogItem>
            {
                new ProductBacklogItem { PbiId = 1, Title = "Draft PBI", Description = "Draft Description", IsDraft = true }
            };
            _mockRepository.GetDraftPbisAsync().Returns(expectedDraftStories);

            // Act
            var result = await _pbiService.GetDraftPbisAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.True(result.First().IsDraft);
            await _mockRepository.Received(1).GetDraftPbisAsync();
        }

        [Fact]
        public async Task CreatePbiAsync_CallsRepositoryAddAsync()
        {
            // Arrange
            var inputPbi = new ProductBacklogItem { Title = "New PBI", Description = "New Description" };
            var createdPbi = new ProductBacklogItem { PbiId = 1, Title = inputPbi.Title, Description = inputPbi.Description };
            _mockRepository.AddAsync(inputPbi).Returns(createdPbi);

            // Act
            var result = await _pbiService.CreatePbiAsync(inputPbi);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(createdPbi.PbiId, result.PbiId);
            Assert.Equal(createdPbi.Title, result.Title);
            await _mockRepository.Received(1).AddAsync(inputPbi);
        }

        [Fact]
        public async Task CommitDraftPbiAsync_UpdatesExistingDraft_ToBacklog()
        {
            // Arrange
            var draftPbi = new ProductBacklogItem
            {
                PbiId = 1,
                Title = "Draft Story",
                Description = "Draft Description",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Medium,
                StoryPoints = PbiPoints.Five,
                Origin = PbiOrigin.AiGenerated,
                IsDraft = true,
                DateCreated = DateTime.UtcNow.AddDays(-1),
                LastUpdated = DateTime.UtcNow.AddDays(-1)
            };

            var updatedPbi = new ProductBacklogItem
            {
                PbiId = 1,
                Title = draftPbi.Title,
                Description = draftPbi.Description,
                Status = draftPbi.Status,
                Priority = draftPbi.Priority,
                StoryPoints = draftPbi.StoryPoints,
                Origin = draftPbi.Origin,
                IsDraft = false,
                DateCreated = draftPbi.DateCreated,
                LastUpdated = DateTime.UtcNow
            };

            _mockRepository.GetByIdAsync(draftPbi.PbiId).Returns(draftPbi);
            _mockRepository.UpdateAsync(Arg.Is<ProductBacklogItem>(s => s.PbiId == draftPbi.PbiId && !s.IsDraft)).Returns(updatedPbi);

            // Act
            var result = await _pbiService.CommitDraftPbiAsync(draftPbi);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(updatedPbi.PbiId, result.PbiId);
            Assert.False(result.IsDraft);
            await _mockRepository.Received(1).GetByIdAsync(draftPbi.PbiId);
            await _mockRepository.Received(1).UpdateAsync(Arg.Is<ProductBacklogItem>(s => s.PbiId == draftPbi.PbiId && !s.IsDraft));
            await _mockRepository.DidNotReceive().AddAsync(Arg.Any<ProductBacklogItem>());
            await _mockRepository.DidNotReceive().DeleteAsync(Arg.Any<int>());
        }

        [Fact]
        public async Task CommitDraftPbiAsync_ThrowsKeyNotFoundException_WhenDraftMissing()
        {
            // Arrange
            var draftPbi = new ProductBacklogItem
            {
                PbiId = 99,
                Title = "Missing Draft",
                Description = "Missing",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Low,
                IsDraft = true,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _mockRepository.GetByIdAsync(draftPbi.PbiId).Returns((ProductBacklogItem?)null);

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(() => _pbiService.CommitDraftPbiAsync(draftPbi));
            await _mockRepository.Received(1).GetByIdAsync(draftPbi.PbiId);
            await _mockRepository.DidNotReceive().AddAsync(Arg.Any<ProductBacklogItem>());
            await _mockRepository.DidNotReceive().DeleteAsync(Arg.Any<int>());
        }

        [Fact]
        public async Task CommitDraftPbiAsync_ThrowsKeyNotFoundException_WhenPbiIsNotDraft()
        {
            // Arrange
            var nonDraftPbi = new ProductBacklogItem
            {
                PbiId = 4,
                Title = "Backlog PBI",
                Description = "Backlog Description",
                Status = PbiStatus.ToDo,
                Priority = PbiPriority.Low,
                IsDraft = false,
                DateCreated = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };

            _mockRepository.GetByIdAsync(nonDraftPbi.PbiId).Returns(nonDraftPbi);

            // Act & Assert
            await Assert.ThrowsAsync<KeyNotFoundException>(() => _pbiService.CommitDraftPbiAsync(nonDraftPbi));
            await _mockRepository.Received(1).GetByIdAsync(nonDraftPbi.PbiId);
            await _mockRepository.DidNotReceive().UpdateAsync(Arg.Any<ProductBacklogItem>());
            await _mockRepository.DidNotReceive().AddAsync(Arg.Any<ProductBacklogItem>());
            await _mockRepository.DidNotReceive().DeleteAsync(Arg.Any<int>());
        }

        [Fact]
        public async Task UpdatePbiAsync_CallsRepositoryUpdateAsync()
        {
            // Arrange
            var updatedPbi = new ProductBacklogItem { PbiId = 1, Title = "Updated PBI", Description = "Updated Description" };
            _mockRepository.UpdateAsync(updatedPbi).Returns(updatedPbi);

            // Act
            var result = await _pbiService.UpdatePbiAsync(updatedPbi);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(updatedPbi.PbiId, result.PbiId);
            Assert.Equal(updatedPbi.Title, result.Title);
            await _mockRepository.Received(1).UpdateAsync(updatedPbi);
        }

        [Fact]
        public async Task DeletePbiAsync_ReturnsTrueWhenSuccessfullyDeleted()
        {
            // Arrange
            var pbiId = 1;
            _mockRepository.DeleteAsync(pbiId).Returns(true);
            // Act
            var result = await _pbiService.DeletePbiAsync(pbiId);

            // Assert
            Assert.True(result);
            await _mockRepository.Received(1).DeleteAsync(pbiId);
        }

        [Fact]
        public async Task DeletePbiAsync_ReturnsFalseWhenPbiNotFound()
        {
            // Arrange
            var pbiId = 999;
            _mockRepository.DeleteAsync(pbiId).Returns(false);
            // Act
            var result = await _pbiService.DeletePbiAsync(pbiId);

            // Assert
            Assert.False(result);
            await _mockRepository.Received(1).DeleteAsync(pbiId);
        }

        [Fact]
        public async Task GenerateAiPbi_ReturnsPbi_WhenValidResponse()
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
            var results = await service.GenerateAiPbis(new List<string> { problemStatement });
            var result = results[0];

            // Assert
            Assert.NotNull(result);
            Assert.Equal(aiResponse.Title, result.Title);
            Assert.Contains(aiResponse.UserStory, result.Description);
            Assert.Contains("Acceptance Criteria:", result.Description);
            Assert.All(aiResponse.AcceptanceCriteria, criteria =>
                Assert.Contains(criteria, result.Description));
            Assert.Equal(PbiStatus.ToDo, result.Status);
            Assert.Equal(PbiPriority.Low, result.Priority);
            Assert.Equal(PbiOrigin.AiGenerated, result.Origin);
        }

        [Fact]
        public async Task GenerateAiPbi_DefaultsToToDoStatus_AndLowPriority()
        {
            // Arrange - verifies the initial Status and Priority values assigned to every
            // AI-generated story before it is triaged in the Backlog.
            var problemStatement = "As a user, I want to filter results";
            var aiResponse = new AiStoryResponse
            {
                Title = "Filter Results Feature",
                UserStory = "As a user, I want to filter results, so that I can find items faster.",
                AcceptanceCriteria = ["Filter dropdown is visible", "Results update on selection"]
            };

            var ollamaResponse = new { response = JsonSerializer.Serialize(aiResponse) };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act
            var results = await service.GenerateAiPbis(new List<string> { problemStatement });
            var result = results[0];

            // Assert
            Assert.Equal(PbiStatus.ToDo, result.Status);
            Assert.Equal(PbiPriority.Low, result.Priority);
        }

        [Fact]
        public async Task GenerateAiPbi_ThrowsInvalidOperationException_WhenOllamaBaseUrlNotConfigured()
        {
            // Arrange
            var problemStatement = "Test problem";
            _mockConfiguration["OllamaBaseUrl"].Returns((string?)null);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _pbiService.GenerateAiPbis(new List<string> { problemStatement }));
            Assert.Equal("No AI provider configured. Set GeminiApiKey or OllamaBaseUrl.", exception.Message);
        }

        [Theory]
        [InlineData("")]
        public async Task GenerateAiPbi_ThrowsInvalidOperationException_WhenOllamaBaseUrlEmpty(string baseUrl)
        {
            // Arrange
            var problemStatement = "Test problem";
            _mockConfiguration["OllamaBaseUrl"].Returns(baseUrl);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => _pbiService.GenerateAiPbis(new List<string> { problemStatement }));
            Assert.Equal("No AI provider configured. Set GeminiApiKey or OllamaBaseUrl.", exception.Message);
        }

        [Fact]
        public async Task GenerateAiPbi_ThrowsInvalidOperationException_WhenOllamaBaseUrlIsWhitespace()
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
                () => service.GenerateAiPbis(new List<string> { problemStatement }));
            Assert.Contains("An unexpected error occurred while calling the Ollama API", exception.Message);
        }

        [Fact]
        public async Task GenerateAiPbi_ThrowsHttpRequestException_WhenApiReturnsError()
        {
            // Arrange
            var problemStatement = "Test problem";
            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse("Error occurred", HttpStatusCode.InternalServerError);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => service.GenerateAiPbis(new List<string> { problemStatement }));
            Assert.Contains("Ollama API request failed with status InternalServerError", exception.Message);
        }

        [Fact]
        public async Task GenerateAiPbi_ThrowsTimeoutException_WhenRequestTimesOut()
        {
            // Arrange
            var problemStatement = "Test problem";
            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithTimeout();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => service.GenerateAiPbis(new List<string> { problemStatement }));
            Assert.Equal("The request to Ollama timed out", exception.Message);
        }

        [Fact]
        public async Task GenerateAiPbi_ThrowsInvalidOperationException_WhenResponseIsEmpty()
        {
            // Arrange
            var problemStatement = "Test problem";
            var ollamaResponse = new { response = "" };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateAiPbis(new List<string> { problemStatement }));
            Assert.Contains("An unexpected error occurred while parsing the AI PBI response", exception.Message);
        }

        [Fact]
        public async Task GenerateAiPbi_ThrowsInvalidOperationException_WhenResponseIsNotValidJson()
        {
            // Arrange
            var problemStatement = "Test problem";
            var ollamaResponse = new { response = "This is not JSON" };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateAiPbis(new List<string> { problemStatement }));
            Assert.Contains("Failed to find a JSON object in the AI response", exception.Message);
        }

        [Fact]
        public async Task GenerateAiPbi_HandlesJsonWithExtraText_ExtractsValidJson()
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
            var results = await service.GenerateAiPbis(new List<string> { problemStatement });
            var result = results[0];

            // Assert
            Assert.NotNull(result);
            Assert.Equal(aiResponse.Title, result.Title);
            Assert.Contains(aiResponse.UserStory, result.Description);
        }

        [Fact]
        public async Task GenerateAiPbi_ThrowsInvalidOperationException_WhenJsonHasInvalidSchema()
        {
            // Arrange
            var problemStatement = "Test problem";
            var invalidResponse = new { title = "Test", description = "Missing required fields" };
            var ollamaResponse = new { response = JsonSerializer.Serialize(invalidResponse) };

            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse(JsonSerializer.Serialize(ollamaResponse), HttpStatusCode.OK);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GenerateAiPbis(new List<string> { problemStatement }));
            Assert.Contains("Failed to parse AI response as JSON", exception.Message);
        }

        [Fact]
        public async Task GenerateAiPbi_SendsCorrectRequestToOllamaApi()
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
            await service.GenerateAiPbis(new List<string> { problemStatement });

            // Assert - Verify the request was made correctly
            Assert.NotNull(_capturedRequest);
            var expectedUrl = $"{baseUrl}api/generate";
            Assert.Equal(expectedUrl, _capturedRequest.RequestUri?.ToString());
            Assert.Equal(HttpMethod.Post, _capturedRequest.Method);

            var requestContent = await _capturedRequest.Content!.ReadAsStringAsync(Xunit.TestContext.Current.CancellationToken);
            var requestObject = JsonSerializer.Deserialize<JsonElement>(requestContent);

            Assert.Equal(model, requestObject.GetProperty("model").GetString());
            Assert.Contains(problemStatement, requestObject.GetProperty("prompt").GetString());
            Assert.False(requestObject.GetProperty("stream").GetBoolean());
        }

        [Fact]
        public async Task GenerateAiPbi_ReturnsCorrectCountAndProperties_WhenMultipleStatements()
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
            var result = await service.GenerateAiPbis(new List<string> { "Statement 1", "Statement 2", "Statement 3" });

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.All(result, story =>
            {
                Assert.Equal(PbiOrigin.AiGenerated, story.Origin);
                Assert.Equal(PbiStatus.ToDo, story.Status);
            });
        }

        [Fact]
        public async Task GenerateAiPbi_PropagatesException_WhenAnyPbiGenerationFails()
        {
            // Arrange
            SetupConfiguration("http://localhost:11434/", "llama2");
            var service = CreateServiceWithResponse("Error body", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(
                () => service.GenerateAiPbis(new List<string> { "Problem 1", "Problem 2" }));
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
            var newService = new PbiService(newHttpClient, _mockConfiguration, _mockRepository);

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

        private PbiService CreateServiceWithResponse(string responseContent, HttpStatusCode statusCode)
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
            return new PbiService(httpClient, _mockConfiguration, _mockRepository);
        }

        private PbiService CreateServiceWithTimeout()
        {
            var handler = new TestHttpMessageHandler((request, cancellation) =>
            {
                _capturedRequest = request;
                throw new TaskCanceledException("The operation was canceled.");
            });

            var httpClient = new HttpClient(handler);
            return new PbiService(httpClient, _mockConfiguration, _mockRepository);
        }
    }
}