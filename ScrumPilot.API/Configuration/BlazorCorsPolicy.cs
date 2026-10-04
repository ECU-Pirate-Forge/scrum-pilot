using Microsoft.AspNetCore.Cors.Infrastructure;

namespace ScrumPilot.API.Configuration;

public static class BlazorCorsPolicy
{
    public const string Name = "AllowBlazor";

    private static readonly string[] AllowedOrigins =
    [
        "http://localhost:5199",
        "http://127.0.0.1:5199",
        "https://localhost:7280",
        "https://127.0.0.1:7280",
        "http://localhost:5299",
        "http://127.0.0.1:5299",
        "https://localhost:7380",
        "https://127.0.0.1:7380",
        "https://scrumpilot-web.onrender.com"
    ];

    public static void Configure(CorsOptions options)
    {
        options.AddPolicy(Name, policy => policy
            .WithOrigins(AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    }
}
