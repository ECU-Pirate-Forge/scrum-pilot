using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Text.Json;

namespace ScrumPilot.Web.Auth
{
    /// <summary>
    /// Custom <see cref="AuthenticationStateProvider"/> that reads a JWT bearer token
    /// from <c>localStorage</c> and parses its claims to build the authentication state.
    /// </summary>
    public class JwtAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IJSRuntime _js;
        private string? _cachedToken;
        private static readonly AuthenticationState Anonymous =
            new(new ClaimsPrincipal(new ClaimsIdentity()));

        /// <summary>Initialises a new instance of <see cref="JwtAuthStateProvider"/>.</summary>
        public JwtAuthStateProvider(IJSRuntime js) => _js = js;

        /// <inheritdoc/>
        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            _cachedToken = await _js.InvokeAsync<string?>("localStorage.getItem", "authToken");
            return string.IsNullOrWhiteSpace(_cachedToken) ? Anonymous : BuildAuthState(_cachedToken);
        }

        /// <summary>
        /// Called by <see cref="AuthService"/> after login or logout to update the cached token
        /// and notify all subscribed components of the new authentication state.
        /// </summary>
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
