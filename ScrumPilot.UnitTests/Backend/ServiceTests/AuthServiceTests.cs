using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Auth;
using ScrumPilot.Web.Services;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ScrumPilot.UnitTests.Backend.ServiceTests
{
    public class AuthServiceTests
    {
        private static readonly string ValidTestToken = CreateTestJwt(new Dictionary<string, string>
        {
            [ClaimTypes.Name] = "Tyler",
            [ClaimTypes.NameIdentifier] = "user-1"
        });

        private static (AuthService service, IJSRuntime mockJs) CreateAuthService(
            HttpStatusCode statusCode, object? responseBody = null)
        {
            var handler = new TestHttpMessageHandler((_, _) =>
            {
                var json = responseBody is not null
                    ? JsonSerializer.Serialize(responseBody)
                    : string.Empty;
                return Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            });

            var mockJs = Substitute.For<IJSRuntime>();
            var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7195/") };
            var authStateProvider = new JwtAuthStateProvider(mockJs);
            var authService = new AuthService(httpClient, mockJs, authStateProvider);

            return (authService, mockJs);
        }

        [Fact]
        public async Task LoginAsync_WhenApiReturnsSuccess_ReturnsTrue()
        {
            // Arrange
            var loginResponse = new LoginResponse { Token = ValidTestToken, UserName = "Tyler" };
            var (service, _) = CreateAuthService(HttpStatusCode.OK, loginResponse);

            // Act
            var result = await service.LoginAsync(new LoginRequest { UserName = "Tyler", Password = "Password1234!" });

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task LoginAsync_WhenApiReturnsUnauthorized_ReturnsFalse()
        {
            // Arrange
            var (service, _) = CreateAuthService(HttpStatusCode.Unauthorized);

            // Act
            var result = await service.LoginAsync(new LoginRequest { UserName = "Tyler", Password = "WrongPass!" });

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task LoginAsync_WhenApiReturnsSuccess_SavesTokenToLocalStorage()
        {
            // Arrange
            var loginResponse = new LoginResponse { Token = ValidTestToken, UserName = "Tyler" };
            var (service, mockJs) = CreateAuthService(HttpStatusCode.OK, loginResponse);

            // Act
            await service.LoginAsync(new LoginRequest { UserName = "Tyler", Password = "Password1234!" });

            // Assert — localStorage.setItem("authToken", token) was called
            await mockJs.Received(1).InvokeAsync<IJSVoidResult>(
                "localStorage.setItem",
                Arg.Is<object?[]?>(args => args != null &&
                                           args[0] != null && args[0].ToString() == "authToken" &&
                                           args[1] != null && args[1].ToString() == ValidTestToken));
        }

        [Fact]
        public async Task LoginAsync_WhenApiReturnsFailure_DoesNotSaveToken()
        {
            // Arrange
            var (service, mockJs) = CreateAuthService(HttpStatusCode.Unauthorized);

            // Act
            await service.LoginAsync(new LoginRequest { UserName = "Tyler", Password = "WrongPass!" });

            // Assert — localStorage.setItem should never have been called
            await mockJs.DidNotReceive().InvokeAsync<IJSVoidResult>(
                "localStorage.setItem",
                Arg.Any<object?[]?>());
        }

        [Fact]
        public async Task LogoutAsync_RemovesTokenFromLocalStorage()
        {
            // Arrange
            var (service, mockJs) = CreateAuthService(HttpStatusCode.OK);

            // Act
            await service.LogoutAsync();

            // Assert — localStorage.removeItem("authToken") was called
            await mockJs.Received(1).InvokeAsync<IJSVoidResult>(
                "localStorage.removeItem",
                Arg.Is<object?[]?>(args => args != null && args[0] != null && args[0].ToString() == "authToken"));
        }

        [Fact]
        public async Task LogoutAsync_AfterLogin_AuthStateIsAnonymous()
        {
            // Arrange
            var loginResponse = new LoginResponse { Token = ValidTestToken, UserName = "Tyler" };
            var (service, mockJs) = CreateAuthService(HttpStatusCode.OK, loginResponse);

            await service.LoginAsync(new LoginRequest { UserName = "Tyler", Password = "Password1234!" });

            // Act
            await service.LogoutAsync();

            // Assert — a fresh provider reading null (default) from localStorage reflects anonymous
            var provider = new JwtAuthStateProvider(mockJs);
            var state = await provider.GetAuthenticationStateAsync();
            Assert.False(state.User.Identity?.IsAuthenticated);
        }

        private static string CreateTestJwt(Dictionary<string, string> claims)
        {
            var header = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
            var payload = Base64UrlEncode(JsonSerializer.Serialize(claims));
            return $"{header}.{payload}.test_signature";
        }

        private static string Base64UrlEncode(string input)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(input))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
