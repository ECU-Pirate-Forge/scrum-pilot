using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ScrumPilot.Web;
using MudBlazor.Services;
using ScrumPilot.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register MudBlazor services with built-in theming
builder.Services.AddMudServices();

// Register simple theme service
builder.Services.AddSingleton<IThemeService, ThemeService>();

// Read ApiBaseUrl from appsettings.json
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7195/";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });

await builder.Build().RunAsync();

