namespace ScrumPilot.Shared.Models;

public class UserDashboardPreference
{
    public string UserId { get; set; } = "";
    public int ProjectId { get; set; }
    public string? PreferencesJson { get; set; }
}
