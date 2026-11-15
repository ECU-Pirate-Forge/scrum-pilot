using ApexCharts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using ScrumPilot.FluentWeb;
using ScrumPilot.FluentWeb.Auth;
using ScrumPilot.FluentWeb.Services;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Configuration.AddEnvironmentVariables();
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddScoped<IAuthService, AuthService>();

var apiBaseUrl = BrowserEndpoints.ApiBase(builder.Configuration, builder.HostEnvironment.BaseAddress);
builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 } telemetry)
{
    var endpoint = new Uri(new Uri(builder.HostEnvironment.BaseAddress), telemetry.TrimEnd('/') + "/");
    builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.Endpoint = new Uri(endpoint, "v1/traces");
        }));
    builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter(options =>
    {
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
        options.Endpoint = new Uri(endpoint, "v1/logs");
    }));
}

builder.Services.AddTransient<AuthHeaderHandler>();
builder.Services.AddHttpClient("API", client => client.BaseAddress = apiBaseUrl)
    .AddHttpMessageHandler<AuthHeaderHandler>();
builder.Services.AddScoped(provider => provider.GetRequiredService<IHttpClientFactory>().CreateClient("API"));
builder.Services.AddScoped<ProjectStateService>();
builder.Services.AddScoped<OrganizationStateService>();
builder.Services.AddScoped<AppThemeService>();
builder.Services.AddScoped<IProjectWorkClient, ProjectWorkClient>();
builder.Services.AddScoped<IAppNotificationService, AppNotificationService>();
builder.Services.AddScoped<IAppDialogService, AppDialogService>();
builder.Services.AddApexCharts();

await builder.Build().RunAsync();
