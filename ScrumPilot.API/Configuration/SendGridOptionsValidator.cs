using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace ScrumPilot.API.Configuration;

public sealed class SendGridOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SendGridOptions>
{
    public ValidateOptionsResult Validate(string? name, SendGridOptions options)
    {
        var failures = new List<string>();
        Require(options.ApiKey, nameof(options.ApiKey), failures);
        Require(options.FromEmail, nameof(options.FromEmail), failures);
        Require(options.FromName, nameof(options.FromName), failures);
        Require(options.InvitationBaseUrl, nameof(options.InvitationBaseUrl), failures);

        if (!string.IsNullOrWhiteSpace(options.FromEmail))
        {
            try
            {
                var address = new MailAddress(options.FromEmail);
                if (!string.Equals(address.Address, options.FromEmail, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add("SendGrid FromEmail must be a valid email address.");
                }
            }
            catch (FormatException)
            {
                failures.Add("SendGrid FromEmail must be a valid email address.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.InvitationBaseUrl))
        {
            if (!Uri.TryCreate(options.InvitationBaseUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                failures.Add("SendGrid InvitationBaseUrl must be an absolute HTTP or HTTPS URL.");
            }
            else if (uri.Scheme == Uri.UriSchemeHttp
                     && (!environment.IsDevelopment() || !uri.IsLoopback))
            {
                failures.Add(
                    "SendGrid InvitationBaseUrl may use HTTP only for a loopback host in Development.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(string value, string name, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"SendGrid {name} is required.");
        }
    }
}
