using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using ScrumPilot.FluentWeb;
using ScrumPilot.FluentWeb.Auth;
using ScrumPilot.FluentWeb.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(provider => provider.GetRequiredService<JwtAuthStateProvider>());
builder.Services.AddScoped<IAuthService, AuthService>();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7195/";

builder.Services.AddTransient<AuthHeaderHandler>();
builder.Services.AddHttpClient("API", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<AuthHeaderHandler>();
builder.Services.AddScoped(provider => provider.GetRequiredService<IHttpClientFactory>().CreateClient("API"));
builder.Services.AddSingleton<ProjectStateService>();
builder.Services.AddScoped<MetricsDashboardService>();

await builder.Build().RunAsync();
