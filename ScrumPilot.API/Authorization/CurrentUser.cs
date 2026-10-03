using System.Security.Claims;

namespace ScrumPilot.API.Authorization;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private const string MissingUserMessage = "An authenticated user identifier is required.";

    public string UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
            {
                throw new InvalidOperationException(MissingUserMessage);
            }

            return userId;
        }
    }

    public string Email
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            var email = principal?.FindFirstValue(ClaimTypes.Email);
            if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("An authenticated user email is required.");
            }
            return email;
        }
    }

    public bool IsInRole(string role) =>
        httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
}
