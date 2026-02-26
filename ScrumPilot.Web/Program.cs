using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ScrumPilot.Web;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register MudBlazor services (for the button component only)
builder.Services.AddMudServices();

// Register simple theme service
builder.Services.AddSingleton<IThemeService, ThemeService>();

// Read ApiBaseUrl from appsettings.json
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7195/";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl + "api/") });

await builder.Build().RunAsync();

