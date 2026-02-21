using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// MudBlazor
builder.Services.AddMudServices();

// Needed because your Generate.razor uses @inject HttpClient
builder.Services.AddHttpClient();

// Blazor (interactive server)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<UI_InputPS.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
