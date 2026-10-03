using System.Net;
using Microsoft.Extensions.Options;
using ScrumPilot.API.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace ScrumPilot.API.Services;

public sealed record InvitationEmailMessage(
    string FromEmail,
    string FromName,
    string ToEmail,
    string Subject,
    string PlainTextContent,
    string HtmlContent);

public interface ISendGridTransport
{
    Task<HttpStatusCode> SendAsync(
        string apiKey,
        InvitationEmailMessage message,
        CancellationToken cancellationToken = default);
}

public sealed class SendGridTransport(HttpClient httpClient) : ISendGridTransport
{
    public async Task<HttpStatusCode> SendAsync(
        string apiKey,
        InvitationEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        var client = new SendGridClient(
            httpClient,
            new SendGridClientOptions { ApiKey = apiKey });
        var response = await client.SendEmailAsync(
            MailHelper.CreateSingleEmail(
                new EmailAddress(message.FromEmail, message.FromName),
                new EmailAddress(message.ToEmail),
                message.Subject,
                message.PlainTextContent,
                message.HtmlContent),
            cancellationToken);
        return response.StatusCode;
    }
}

public sealed class SendGridInvitationEmailSender(
    IOptions<SendGridOptions> options,
    ISendGridTransport transport,
    ILogger<SendGridInvitationEmailSender> logger) : IInvitationEmailSender
{
    public async Task SendAsync(
        string email,
        string token,
        CancellationToken cancellationToken = default)
    {
        SendGridOptions configuration;
        try
        {
            configuration = options.Value;
        }
        catch (OptionsValidationException exception)
        {
            logger.LogError("SendGrid invitation configuration is invalid.");
            throw new InvitationDeliveryException(
                "SendGrid configuration is invalid.",
                exception);
        }
        var url = BuildInvitationUrl(configuration.InvitationBaseUrl, token);
        var message = new InvitationEmailMessage(
            configuration.FromEmail,
            configuration.FromName,
            email,
            "You are invited to ScrumPilot",
            $"Accept your ScrumPilot organization invitation: {url}",
            $"<p>You are invited to join an organization in ScrumPilot.</p><p><a href=\"{WebUtility.HtmlEncode(url)}\">Accept invitation</a></p>");
        HttpStatusCode statusCode;
        try
        {
            statusCode = await transport.SendAsync(
                configuration.ApiKey,
                message,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                "SendGrid invitation delivery failed with {ExceptionType}.",
                exception.GetType().Name);
            throw new InvitationDeliveryException(
                "SendGrid invitation delivery failed.",
                exception);
        }
        if ((int)statusCode is < 200 or >= 300)
        {
            logger.LogWarning(
                "SendGrid invitation delivery returned HTTP status {StatusCode}.",
                (int)statusCode);
            throw new InvitationDeliveryException(
                $"SendGrid invitation delivery returned HTTP status {(int)statusCode}.");
        }
    }

    private static string BuildInvitationUrl(string baseUrl, string token)
    {
        var builder = new UriBuilder(baseUrl);
        var parameter = $"token={Uri.EscapeDataString(token)}";
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? parameter
            : $"{builder.Query.TrimStart('?')}&{parameter}";
        return builder.Uri.AbsoluteUri;
    }
}
