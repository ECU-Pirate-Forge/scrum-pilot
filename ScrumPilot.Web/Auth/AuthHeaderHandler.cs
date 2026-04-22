using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ScrumPilot.Web.Auth
{
    /// <summary>
    /// Delegating HTTP handler that attaches the JWT bearer token to every outgoing API request.
    /// If the stored token has expired, it clears the token, resets auth state, and redirects to
    /// the login page instead of sending the request.
    /// </summary>
    public class AuthHeaderHandler : DelegatingHandler
    {
        private readonly IJSRuntime _js;
        private readonly JwtAuthStateProvider _authStateProvider;
        private readonly NavigationManager _navigation;

        /// <summary>Initialises a new instance of <see cref="AuthHeaderHandler"/>.</summary>
        public AuthHeaderHandler(IJSRuntime js, AuthenticationStateProvider authStateProvider, NavigationManager navigation)
        {
            _js = js;
            _authStateProvider = (JwtAuthStateProvider)authStateProvider;
            _navigation = navigation;
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
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
                else
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }

        /// <summary>
        /// Decodes the JWT payload and checks whether the <c>exp</c> claim is in the past.
        /// Returns <c>false</c> (not expired) if the token cannot be decoded.
        /// </summary>
        private static bool IsTokenExpired(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return true;
                var payload = parts[1];
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }
                var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    Convert.FromBase64String(payload));
                if (json is null || !json.TryGetValue("exp", out var expEl)) return false;
                var exp = expEl.GetInt64();
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp;
            }
            catch
            {
                return false;
            }
        }
    }
}
