using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ScrumPilot.Web;
using ScrumPilot.Web.Auth;
using ScrumPilot.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
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

// Read ApiBaseUrl from appsettings.json
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5219/";

// HttpClient that automatically attaches the JWT Bearer token to every request
builder.Services.AddTransient<AuthHeaderHandler>();
builder.Services.AddHttpClient("API", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHeaderHandler>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("API"));
builder.Services.AddScoped<ScrumPilot.Web.Services.MetricsDashboardService>();
builder.Services.AddSingleton<ScrumPilot.Web.Services.ProjectStateService>();

await builder.Build().RunAsync();

