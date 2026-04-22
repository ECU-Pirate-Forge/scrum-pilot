namespace ScrumPilot.Shared.Models
{
    /// <summary>
    /// Payload sent to the authentication endpoint to obtain a JWT token.
    /// </summary>
    public class LoginRequest
    {
        /// <summary>The user's login name.</summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>The user's plain-text password (transmitted over HTTPS).</summary>
        public string Password { get; set; } = string.Empty;
    }
}
