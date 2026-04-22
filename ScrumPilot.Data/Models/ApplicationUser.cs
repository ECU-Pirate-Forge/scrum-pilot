using Microsoft.AspNetCore.Identity;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.Data.Models
{
    /// <summary>
    /// Extends ASP.NET Core Identity's <see cref="IdentityUser"/> with ScrumPilot-specific profile fields.
    /// </summary>
    public class ApplicationUser : IdentityUser
    {
        /// <summary>The user's Discord username used by the bot integration.</summary>
        public string? DiscordUsername { get; set; }

        /// <summary>The user's preferred UI colour scheme; defaults to Light.</summary>
        public UiPreference UiPreference { get; set; } = UiPreference.Light;

        /// <summary>The project automatically selected when this user logs in.</summary>
        public int? DefaultProjectId { get; set; }
    }
}
