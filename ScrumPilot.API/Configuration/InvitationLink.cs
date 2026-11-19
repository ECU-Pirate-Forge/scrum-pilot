namespace ScrumPilot.API.Configuration;

internal static class InvitationLink
{
    public static bool TryGetBaseUri(string? configuredUrl, IHostEnvironment environment, out Uri? uri)
    {
        return Uri.TryCreate(configuredUrl, UriKind.Absolute, out uri)
            && string.IsNullOrEmpty(uri.UserInfo)
            && (uri.Scheme == Uri.UriSchemeHttps
                || (uri.Scheme == Uri.UriSchemeHttp && environment.IsDevelopment() && uri.IsLoopback));
    }

    public static string Build(Uri baseUri, string token)
    {
        var builder = new UriBuilder(baseUri);
        var parameter = $"token={Uri.EscapeDataString(token)}";
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? parameter
            : $"{builder.Query.TrimStart('?')}&{parameter}";
        return builder.Uri.AbsoluteUri;
    }
}
