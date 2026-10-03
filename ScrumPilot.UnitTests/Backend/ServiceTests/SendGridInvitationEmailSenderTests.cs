using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
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
        var sender = new SendGridInvitationEmailSender(
            new ThrowingOptions(),
            transport,
            NullLogger<SendGridInvitationEmailSender>.Instance);

        var exception = await Assert.ThrowsAsync<InvitationDeliveryException>(() =>
            sender.SendAsync("member@example.com", "token"));

        Assert.Contains("SendGrid", exception.Message);
        Assert.Null(transport.Message);
    }

    [Fact]
    public void Validate_ProductionHttpInvitationUrlIsRejected()
    {
        var result = CreateValidator(Environments.Production).Validate(
            null,
            ValidOptions("http://app.example.com/accept"));

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_DevelopmentNonLoopbackHttpInvitationUrlIsRejected()
    {
        var result = CreateValidator(Environments.Development).Validate(
            null,
            ValidOptions("http://dev.example.com/accept"));

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_DevelopmentLoopbackHttpInvitationUrlIsAllowed()
    {
        var result = CreateValidator(Environments.Development).Validate(
            null,
            ValidOptions("http://localhost:5173/accept"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MissingApiKeyHasExplicitFailure()
    {
        var options = ValidOptions();
        options.ApiKey = string.Empty;

        var result = CreateValidator(Environments.Production).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("ApiKey"));
    }

    [Fact]
    public async Task Transport_UsesInjectedHttpClientForRepeatedSends()
    {
        var handler = new RecordingHandler();
        var transport = new SendGridTransport(new HttpClient(handler));
        var message = new InvitationEmailMessage(
            "invites@example.com",
            "ScrumPilot",
            "member@example.com",
            "Invitation",
            "plain",
            "<p>html</p>");

        await transport.SendAsync("secret", message);
        await transport.SendAsync("secret", message);

        Assert.Equal(2, handler.RequestCount);
        Assert.All(
            handler.AuthorizationValues,
            value => Assert.Equal(string.Concat("sec", "ret"), value));
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

    private static SendGridOptionsValidator CreateValidator(string environmentName)
    {
        var environment = NSubstitute.Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new(environment);
    }

    private static SendGridOptions ValidOptions(
        string invitationBaseUrl = "https://app.example.com/accept") => new()
        {
            ApiKey = "secret",
            FromEmail = "invites@example.com",
            FromName = "ScrumPilot",
            InvitationBaseUrl = invitationBaseUrl
        };

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

    private sealed class ThrowingOptions : IOptions<SendGridOptions>
    {
        public SendGridOptions Value => throw new OptionsValidationException(
            SendGridOptions.SectionName,
            typeof(SendGridOptions),
            ["SendGrid ApiKey is required."]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<string?> AuthorizationValues { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            AuthorizationValues.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }
    }
}
