using Microsoft.JSInterop;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Auth;
using System.Net.Http.Json;

namespace ScrumPilot.Web.Services
{
    /// <summary>
    /// Implements <see cref="IAuthService"/> by posting credentials to the API,
    /// storing the returned JWT in <c>localStorage</c>, and notifying
    /// <see cref="JwtAuthStateProvider"/> of authentication state changes.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly HttpClient _http;
        private readonly IJSRuntime _js;
        private readonly JwtAuthStateProvider _authStateProvider;

        /// <summary>Initialises a new instance of <see cref="AuthService"/>.</summary>
        public AuthService(HttpClient http, IJSRuntime js, JwtAuthStateProvider authStateProvider)
        {
            _http = http;
            _js = js;
            _authStateProvider = authStateProvider;
        }

        /// <inheritdoc/>
        public async Task<bool> LoginAsync(LoginRequest request)
        {
            var response = await _http.PostAsJsonAsync("api/auth/login", request);
            if (!response.IsSuccessStatusCode) return false;

            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
            await _js.InvokeVoidAsync("localStorage.setItem", "authToken", loginResponse!.Token);
            _authStateProvider.NotifyAuthChanged(loginResponse.Token);
            return true;
        }

        /// <inheritdoc/>
        public async Task LogoutAsync()
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", "authToken");
            _authStateProvider.NotifyAuthChanged(null);
        }
    }
}
