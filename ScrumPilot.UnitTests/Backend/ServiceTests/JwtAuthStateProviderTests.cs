using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using ScrumPilot.Web.Auth;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace ScrumPilot.UnitTests.Backend.ServiceTests
{
    public class JwtAuthStateProviderTests
    {
        [Fact]
        public async Task GetAuthenticationStateAsync_WhenTokenInStorage_ReturnsAuthenticatedUser()
        {
            // Arrange
            var mockJs = Substitute.For<IJSRuntime>();
            mockJs.InvokeAsync<string?>("localStorage.getItem", Arg.Any<object?[]?>())
                  .Returns(new ValueTask<string?>(CreateTestJwt(new Dictionary<string, string>
                  {
                      [ClaimTypes.Name] = "Tyler",
                      [ClaimTypes.NameIdentifier] = "user-1"
                  })));
            var provider = new JwtAuthStateProvider(mockJs);

            // Act
            var state = await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.True(state.User.Identity?.IsAuthenticated);
        }

        [Fact]
        public async Task GetAuthenticationStateAsync_WhenTokenInStorage_ReturnsCorrectUsername()
        {
            // Arrange
            var mockJs = Substitute.For<IJSRuntime>();
            mockJs.InvokeAsync<string?>("localStorage.getItem", Arg.Any<object?[]?>())
                  .Returns(new ValueTask<string?>(CreateTestJwt(new Dictionary<string, string>
                  {
                      [ClaimTypes.Name] = "Tyler",
                      [ClaimTypes.NameIdentifier] = "user-1"
                  })));
            var provider = new JwtAuthStateProvider(mockJs);

            // Act
            var state = await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.Equal("Tyler", state.User.Identity?.Name);
        }

        [Fact]
        public async Task GetAuthenticationStateAsync_WhenNoTokenInStorage_ReturnsAnonymousUser()
        {
            // Arrange — no setup needed; Substitute returns default(ValueTask<string?>) which awaits to null
            var provider = new JwtAuthStateProvider(Substitute.For<IJSRuntime>());

            // Act
            var state = await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.False(state.User.Identity?.IsAuthenticated);
        }

        [Fact]
        public void NotifyAuthChanged_WithValidToken_RaisesEventWithAuthenticatedState()
        {
            // Arrange
            var token = CreateTestJwt(new Dictionary<string, string>
            {
                [ClaimTypes.Name] = "Tyler",
                [ClaimTypes.NameIdentifier] = "user-1"
            });
            var provider = new JwtAuthStateProvider(Substitute.For<IJSRuntime>());

            AuthenticationState? capturedState = null;
            provider.AuthenticationStateChanged += async task => capturedState = await task;

            // Act
            provider.NotifyAuthChanged(token);

            // Assert — Task.FromResult completes synchronously so capturedState is set immediately
            Assert.NotNull(capturedState);
            Assert.True(capturedState!.User.Identity?.IsAuthenticated);
        }

        [Fact]
        public void NotifyAuthChanged_WithNull_RaisesEventWithAnonymousState()
        {
            // Arrange
            var provider = new JwtAuthStateProvider(Substitute.For<IJSRuntime>());

            AuthenticationState? capturedState = null;
            provider.AuthenticationStateChanged += async task => capturedState = await task;

            // Act
            provider.NotifyAuthChanged(null);

            // Assert
            Assert.NotNull(capturedState);
            Assert.False(capturedState!.User.Identity?.IsAuthenticated);
        }

        // Builds a properly Base64Url-encoded JWT with a fake signature.
        // JwtAuthStateProvider only parses claims from the payload, so no real
        // signing key is needed for these tests.
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
