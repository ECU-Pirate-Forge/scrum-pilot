using Microsoft.Extensions.Configuration;
using ScrumPilot.Client;
using ScrumPilot.FluentWeb.Services;
using Xunit;

namespace ScrumPilot.FluentWeb.Tests.Account;

public sealed class BrowserEndpointTests
{
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
