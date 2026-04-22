using ScrumPilot.Shared.Models;

namespace ScrumPilot.Web.Services
{
    /// <summary>
    /// Client-side authentication contract for logging in and out of the application.
    /// </summary>
    public interface IAuthService
    {
        /// <summary>
        /// Submits the supplied credentials to the API and, on success, stores the JWT
        /// token and notifies the authentication state provider.
        /// Returns <c>true</c> if login succeeded.
        /// </summary>
        Task<bool> LoginAsync(LoginRequest request);

        /// <summary>Clears the stored JWT token and notifies the authentication state provider.</summary>
        Task LogoutAsync();
    }
}
