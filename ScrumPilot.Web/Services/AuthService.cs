using Microsoft.JSInterop;
using ScrumPilot.Shared.Models;
using ScrumPilot.Web.Auth;
using System.Net.Http.Json;

namespace ScrumPilot.Web.Services
{
    public class AuthService : IAuthService
    {
        private readonly HttpClient _http;
        private readonly IJSRuntime _js;
        private readonly JwtAuthStateProvider _authStateProvider;

        public AuthService(HttpClient http, IJSRuntime js, JwtAuthStateProvider authStateProvider)
        {
            _http = http;
            _js = js;
            _authStateProvider = authStateProvider;
        }

        public async Task<bool> LoginAsync(LoginRequest request)
        {
            var response = await _http.PostAsJsonAsync("api/auth/login", request);
            if (!response.IsSuccessStatusCode) return false;

            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
            await _js.InvokeVoidAsync("localStorage.setItem", "authToken", loginResponse!.Token);
            _authStateProvider.NotifyAuthChanged(loginResponse.Token);
            return true;
        }

        public async Task LogoutAsync()
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", "authToken");
            _authStateProvider.NotifyAuthChanged(null);
        }
    }
}
