using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using ScrumPilot.FluentWeb.Auth;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.FluentWeb.Services;

/// <summary>
/// Implements <see cref="IAuthService"/> by posting credentials to the API,
/// storing the returned JWT in <c>localStorage</c>, and notifying the auth state provider.
/// </summary>
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
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (loginResponse is null)
        {
            return false;
        }

        await _js.InvokeVoidAsync("localStorage.setItem", "authToken", loginResponse.Token);
        _authStateProvider.NotifyAuthChanged(loginResponse.Token);
        return true;
    }

    public async Task LogoutAsync()
    {
        await _js.InvokeVoidAsync("localStorage.removeItem", "authToken");
        _authStateProvider.NotifyAuthChanged(null);
    }
}
