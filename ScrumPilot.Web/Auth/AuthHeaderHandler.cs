using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ScrumPilot.Web.Auth
{
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
