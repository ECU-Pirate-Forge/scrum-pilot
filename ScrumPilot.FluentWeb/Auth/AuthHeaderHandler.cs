using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace ScrumPilot.FluentWeb.Auth;

/// <summary>
/// Delegating HTTP handler that attaches the JWT bearer token to every outgoing API request.
/// If the stored token has expired, it clears the token and redirects the user to the login page.
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly IJSRuntime _js;
    private readonly JwtAuthStateProvider _authStateProvider;
    private readonly NavigationManager _navigation;

    public AuthHeaderHandler(IJSRuntime js, AuthenticationStateProvider authStateProvider, NavigationManager navigation)
    {
        _js = js;
        _authStateProvider = (JwtAuthStateProvider)authStateProvider;
        _navigation = navigation;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _js.InvokeAsync<string?>("localStorage.getItem", "authToken");
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (IsTokenExpired(token))
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", "authToken");
                _authStateProvider.NotifyAuthChanged(null);
                _navigation.NavigateTo("/login");
                return new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsTokenExpired(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return true;
            }

            var payload = parts[1];
            switch (payload.Length % 4)
            {
                case 2:
                    payload += "==";
                    break;
                case 3:
                    payload += "=";
                    break;
            }

            var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                Convert.FromBase64String(payload));
            if (json is null || !json.TryGetValue("exp", out var expElement))
            {
                return false;
            }

            var exp = expElement.GetInt64();
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp;
        }
        catch (FormatException)
        {
            return true;
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
