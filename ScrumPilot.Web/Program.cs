using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ScrumPilot.Web;
using ScrumPilot.Web.Auth;
using ApexCharts;
using ScrumPilot.Web.Services;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Configuration.AddEnvironmentVariables();
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register MudBlazor services with built-in theming
builder.Services.AddMudServices();

// Auth services
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddScoped<IAuthService, AuthService>();

// Aspire's browser bridge contains only public proxy endpoints, never private connection strings.
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

// HttpClient that automatically attaches the JWT Bearer token to every request
builder.Services.AddTransient<AuthHeaderHandler>();
builder.Services.AddHttpClient("API", client => client.BaseAddress = apiBaseUrl)
    .AddHttpMessageHandler<AuthHeaderHandler>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("API"));
builder.Services.AddApexCharts();
builder.Services.AddScoped<ProjectStateService>();
builder.Services.AddScoped<OrganizationStateService>();

await builder.Build().RunAsync();
