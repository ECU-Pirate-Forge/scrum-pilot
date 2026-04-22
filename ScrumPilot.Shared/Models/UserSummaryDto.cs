namespace ScrumPilot.Shared.Models;

/// <summary>
/// Lightweight projection of an application user used for assignment dropdowns and avatar initials.
/// </summary>
public class UserSummaryDto
{
    /// <summary>The user's Identity ID.</summary>
    public string Id { get; set; } = "";

    /// <summary>The user's login/display name.</summary>
    public string UserName { get; set; } = "";
}
