using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace ScrumPilot.FluentWeb.Auth;

/// <summary>
/// Custom authentication state provider that reads the JWT from localStorage and builds a claims principal.
/// </summary>
public class JwtAuthStateProvider : AuthenticationStateProvider
{
    private readonly IJSRuntime _js;
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public JwtAuthStateProvider(IJSRuntime js)
    {
        _js = js;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _js.InvokeAsync<string?>("localStorage.getItem", "authToken");
        return string.IsNullOrWhiteSpace(token) ? Anonymous : BuildAuthState(token);
    }

    public void NotifyAuthChanged(string? token)
    {
        var state = string.IsNullOrWhiteSpace(token) ? Anonymous : BuildAuthState(token);
        NotifyAuthenticationStateChanged(Task.FromResult(state));
    }

    private static AuthenticationState BuildAuthState(string token)
    {
        try
        {
            var claims = ParseClaimsFromJwt(token);
            var identity = new ClaimsIdentity(claims, "jwt");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (FormatException)
        {
            return Anonymous;
        }
        catch (JsonException)
        {
            return Anonymous;
        }
    }

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var jsonBytes = ParseBase64WithoutPadding(payload);
        var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);
        if (keyValuePairs is null)
        {
            return [];
        }

        return keyValuePairs.Select(kvp => new Claim(kvp.Key, kvp.Value.ToString()));
    }

    private static byte[] ParseBase64WithoutPadding(string base64)
    {
        switch (base64.Length % 4)
        {
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
        }

        return Convert.FromBase64String(base64);
    }
}
