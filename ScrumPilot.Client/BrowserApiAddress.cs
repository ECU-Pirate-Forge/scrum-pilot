namespace ScrumPilot.Client;

public static class BrowserApiAddress
{
    public static Uri Resolve(string appBase, string? serviceAddress, string? directAddress)
    {
        var address = serviceAddress ?? directAddress
            ?? throw new InvalidOperationException("ApiBaseUrl must be configured for direct runs.");
        var browserBase = new Uri(appBase);
        var uri = new Uri(browserBase, address.TrimEnd('/') + "/");
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("The browser API endpoint must be a public HTTP URL.");

        const string proxyPath = "_api/webapi/";
        if (serviceAddress is not null &&
            (uri.AbsolutePath == "/" + proxyPath || uri.AbsolutePath == new Uri(browserBase, proxyPath).AbsolutePath))
        {
            // The gateway can advertise its local origin even when the browser arrives through a tunnel.
            return new UriBuilder(uri)
            {
                Scheme = browserBase.Scheme,
                Host = browserBase.Host,
                Port = browserBase.Port
            }.Uri;
        }

        return uri;
    }
}
