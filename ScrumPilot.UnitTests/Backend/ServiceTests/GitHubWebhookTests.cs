using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using ScrumPilot.API.Configuration;
using ScrumPilot.API.Services;
using Xunit;

namespace ScrumPilot.UnitTests.Backend.ServiceTests;

public sealed class GitHubWebhookTests
{
    [Fact]
    public void SignatureValidatesExactBodyAndRejectsTampering()
    {
        var body = Encoding.UTF8.GetBytes("{\"action\":\"synchronize\"}");
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), body));
        var client = new GitHubAppClient(new HttpClient(), Options.Create(new GitHubAppOptions { WebhookSecret = "test-secret" }));
        Assert.True(client.VerifyWebhook(body, signature));
        Assert.False(client.VerifyWebhook(Encoding.UTF8.GetBytes("{}"), signature));
        Assert.False(client.VerifyWebhook(body, "sha256=" + new string('z', 64)));
        Assert.False(client.VerifyWebhook(body, ""));
    }
}
