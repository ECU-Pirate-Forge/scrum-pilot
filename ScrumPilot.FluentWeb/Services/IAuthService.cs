using ScrumPilot.Shared.Models;

namespace ScrumPilot.FluentWeb.Services;

public interface IAuthService
{
    Task<bool> LoginAsync(LoginRequest request);
    Task LogoutAsync();
}
