namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Returned by the authentication endpoint after a successful login.
    /// </summary>
    public class LoginResponse
    {
        /// <summary>Signed JWT bearer token to include in subsequent API requests.</summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>The authenticated user's display name.</summary>
        public string UserName { get; set; } = string.Empty;
    }
}
