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

        /// <summary>The organization automatically selected when this user logs in.</summary>
        public int? DefaultOrganizationId { get; set; }

        /// <summary>The organizations this user belongs to.</summary>
        public ICollection<OrganizationMembership> OrganizationMemberships { get; set; } = [];

        /// <summary>The projects this user can access through an explicit grant.</summary>
        public ICollection<ProjectMembership> ProjectMemberships { get; set; } = [];
    }
}
