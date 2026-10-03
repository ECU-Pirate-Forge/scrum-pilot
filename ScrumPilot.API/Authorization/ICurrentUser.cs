namespace ScrumPilot.API.Authorization;

public interface ICurrentUser
{
    string UserId { get; }

    bool IsInRole(string role);
}
