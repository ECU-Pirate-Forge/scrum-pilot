using Microsoft.Extensions.Configuration;
using ScrumPilot.Client;

namespace ScrumPilot.Web.Services;

public static class BrowserEndpoints
{
    public static Uri ApiBase(IConfiguration configuration, string appBase) =>
        BrowserApiAddress.Resolve(appBase,
            configuration["services:webapi:https:0"] ?? configuration["services:webapi:http:0"],
            configuration["ApiBaseUrl"]);
}
