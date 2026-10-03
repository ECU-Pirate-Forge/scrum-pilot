using System.ComponentModel.DataAnnotations;

namespace ScrumPilot.API.Configuration;

public sealed class SendGridOptions
{
    public const string SectionName = "SendGrid";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string FromEmail { get; set; } = string.Empty;

    [Required]
    public string FromName { get; set; } = string.Empty;

    [Required, Url]
    public string InvitationBaseUrl { get; set; } = string.Empty;
}
