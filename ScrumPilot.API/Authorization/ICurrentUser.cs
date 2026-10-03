namespace ScrumPilot.API.Authorization;

public interface ICurrentUser
{
    string UserId { get; }

    string Email { get; }

    bool IsInRole(string role);
}
