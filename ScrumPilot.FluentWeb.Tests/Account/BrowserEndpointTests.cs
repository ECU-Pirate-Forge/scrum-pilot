using Microsoft.Extensions.Configuration;
using ScrumPilot.Client;
using ScrumPilot.FluentWeb.Services;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Account;

public sealed class BrowserEndpointTests
{
    [Theory]
    [InlineData("fluentweb", "https")]
    [InlineData("web", "http")]
    public void Gateway_proxy_uses_the_tunnel_origin_instead_of_localhost(string site, string serviceScheme)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"services:webapi:{serviceScheme}:0"] = $"https://localhost:7380/{site}/_api/webapi"
        }).Build();

        var api = BrowserEndpoints.ApiBase(configuration, $"https://tunnel.example.test/{site}/");

        Assert.Equal($"https://tunnel.example.test/{site}/_api/webapi/api/auth/login",
            new Uri(api, "api/auth/login").AbsoluteUri);
    }

    [Fact]
    public void Nonproxy_service_reference_keeps_its_configured_origin()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["services:webapi:https:0"] = "https://api.example.test"
        }).Build();

        Assert.Equal("https://api.example.test/",
            BrowserEndpoints.ApiBase(configuration, "https://gateway/fluentweb/").AbsoluteUri);
    }

    [Fact]
    public void Gateway_endpoint_wins_over_local_api_and_keeps_signalr_prefix()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["services:webapi:http:0"] = "/_api/webapi",
            ["ApiBaseUrl"] = "http://localhost:5219/"
        }).Build();
        var api = BrowserEndpoints.ApiBase(configuration, "https://gateway/fluentweb/");
        Assert.Equal("https://gateway/_api/webapi/", api.AbsoluteUri);
        Assert.Equal("https://gateway/_api/webapi/hubs/organizations/2/planning-poker", new Uri(api, OrganizationRoutes.PokerHub(2)).AbsoluteUri);
        Assert.Equal(2, OrganizationRoutes.OrganizationId(new Uri("https://gateway/fluentweb/organizations/2/backlog?projectId=7")));
        Assert.Equal("https://gateway/fluentweb/organizations/2/backlog?projectId=7",
            new Uri(new Uri("https://gateway/fluentweb/"), OrganizationRoutes.Page(2, "backlog", 7)).AbsoluteUri);
    }

    [Fact]
    public void Direct_run_uses_the_existing_wwwroot_api_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApiBaseUrl"] = "http://localhost:5219"
        }).Build();
        Assert.Equal("http://localhost:5219/", BrowserEndpoints.ApiBase(configuration, "http://localhost:5000/").AbsoluteUri);
    }
}
