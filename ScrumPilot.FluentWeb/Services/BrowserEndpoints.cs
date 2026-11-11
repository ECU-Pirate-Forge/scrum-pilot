using Microsoft.Extensions.Configuration;

namespace ScrumPilot.FluentWeb.Services;

public static class BrowserEndpoints
{
    public static Uri ApiBase(IConfiguration configuration, string appBase)
    {
        var address = configuration["services:webapi:https:0"]
            ?? configuration["services:webapi:http:0"]
            ?? configuration["ApiBaseUrl"]
            ?? throw new InvalidOperationException("ApiBaseUrl must be configured for direct runs.");
        var uri = new Uri(new Uri(appBase), address.TrimEnd('/') + "/");
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("The browser API endpoint must be a public HTTP URL.");
        return uri;
    }
}
