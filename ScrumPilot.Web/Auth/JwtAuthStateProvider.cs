using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Text.Json;

namespace ScrumPilot.Web.Auth
{
    public class JwtAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _js;
        private string? _cachedToken;
        private static readonly AuthenticationState Anonymous =
            new(new ClaimsPrincipal(new ClaimsIdentity()));

        public JwtAuthStateProvider(IJSRuntime js) => _js = js;

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            _cachedToken = await _js.InvokeAsync<string?>("localStorage.getItem", "authToken");
            return string.IsNullOrWhiteSpace(_cachedToken) ? Anonymous : BuildAuthState(_cachedToken);
        }

        // Called by AuthService after login/logout — updates the cached token and notifies the app
        public void NotifyAuthChanged(string? token)
        {
            _cachedToken = token;
            var state = token is null ? Anonymous : BuildAuthState(token);
            NotifyAuthenticationStateChanged(Task.FromResult(state));
        }

        private static AuthenticationState BuildAuthState(string token)
        {
            var claims = ParseClaimsFromJwt(token);
            var identity = new ClaimsIdentity(claims, "jwt");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }

        private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var payload = jwt.Split('.')[1];
            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes)!;
            return keyValuePairs.Select(kvp => new Claim(kvp.Key, kvp.Value.ToString()));
        }

        private static byte[] ParseBase64WithoutPadding(string base64)
        {
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}
