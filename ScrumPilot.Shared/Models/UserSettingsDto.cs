namespace ScrumPilot.Shared.Models;

public class UserSettingsDto
{
    public string? Email { get; set; }
    public string? DiscordUsername { get; set; }
    public UiPreference UiPreference { get; set; }
    public int? DefaultProjectId { get; set; }
}
