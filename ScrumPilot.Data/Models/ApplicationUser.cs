using Microsoft.AspNetCore.Identity;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? DiscordUsername { get; set; }
        public UiPreference UiPreference { get; set; } = UiPreference.Light;
    }
}
