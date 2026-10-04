using ScrumPilot.Shared.Models;

namespace ScrumPilot.FluentWeb.Services;

/// <summary>
/// Client-side authentication contract for logging in and out of the application.
/// </summary>
public interface IAuthService
{
    Task<bool> LoginAsync(LoginRequest request);

    Task LogoutAsync();
}
