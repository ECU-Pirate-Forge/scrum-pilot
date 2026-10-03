using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Services;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class SendGridInvitationEmailSenderTests
{
    [Fact]
    public async Task SendAsync_BuildsEscapedUrlAndMessage()
    {
        var transport = new FakeTransport(HttpStatusCode.Accepted);
        var sender = CreateSender(
            transport,
            new()
            {
                ApiKey = "secret",
                FromEmail = "invites@example.com",
                FromName = "ScrumPilot",
                InvitationBaseUrl = "https://app.example.com/accept?source=email"
            });

        await sender.SendAsync("member@example.com", "abc+/ =");

        Assert.Equal("secret", transport.ApiKey);
        Assert.Equal("invites@example.com", transport.Message!.FromEmail);
        Assert.Equal("ScrumPilot", transport.Message.FromName);
        Assert.Equal("member@example.com", transport.Message.ToEmail);
        Assert.Contains(
            "https://app.example.com/accept?source=email&amp;token=abc%2B%2F%20%3D",
            transport.Message.HtmlContent);
        Assert.Contains(
            "https://app.example.com/accept?source=email&token=abc%2B%2F%20%3D",
            transport.Message.PlainTextContent);
    }

    [Fact]
    public async Task SendAsync_MissingOptionsFailsExplicitlyWithoutCallingProvider()
    {
        var transport = new FakeTransport(HttpStatusCode.Accepted);
        var sender = CreateSender(transport, new());

        var exception = await Assert.ThrowsAsync<InvitationDeliveryException>(() =>
            sender.SendAsync("member@example.com", "token"));

        Assert.Contains("SendGrid", exception.Message);
        Assert.Null(transport.Message);
    }

    [Fact]
    public async Task SendAsync_NonSuccessProviderResponseIsDeliveryFailure()
    {
        var sender = CreateSender(
            new FakeTransport(HttpStatusCode.ServiceUnavailable),
            new()
            {
                ApiKey = "secret",
                FromEmail = "invites@example.com",
                FromName = "ScrumPilot",
                InvitationBaseUrl = "https://app.example.com/accept"
            });

        var exception = await Assert.ThrowsAsync<InvitationDeliveryException>(() =>
            sender.SendAsync("member@example.com", "token"));

        Assert.Contains("503", exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
        Assert.DoesNotContain("token", exception.Message);
    }

    private static SendGridInvitationEmailSender CreateSender(
        ISendGridTransport transport,
        SendGridOptions options) =>
        new(
            Options.Create(options),
            transport,
            NullLogger<SendGridInvitationEmailSender>.Instance);

    private sealed class FakeTransport(HttpStatusCode statusCode) : ISendGridTransport
    {
        public string? ApiKey { get; private set; }
        public InvitationEmailMessage? Message { get; private set; }

        public Task<HttpStatusCode> SendAsync(
            string apiKey,
            InvitationEmailMessage message,
            CancellationToken cancellationToken = default)
        {
            ApiKey = apiKey;
            Message = message;
            return Task.FromResult(statusCode);
        }
    }
}
