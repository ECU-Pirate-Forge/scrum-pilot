using Microsoft.JSInterop;

namespace ScrumPilot.Web.Services;

public class ThemeService : IThemeService
{
    private bool _isDarkMode = false; // Default to light mode
    private readonly IJSRuntime _jsRuntime;

    public event Action? OnThemeChanged;

    public bool IsDarkMode => _isDarkMode;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Always start in light mode for now
            // In the future, you can uncomment this to load saved preference:
            // var savedTheme = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "darkMode");
            // _isDarkMode = savedTheme == "true";

            // For now, always start in light mode
            _isDarkMode = false;
        }
        catch
        {
            // If localStorage is not available (SSR), default to light mode
            _isDarkMode = false;
        }
    }

    public async void ToggleDarkMode()
    {
        await SetDarkMode(!_isDarkMode);
    }

    public async Task SetDarkMode(bool isDark)
    {
        _isDarkMode = isDark;

        try
        {
            // Save theme preference to localStorage
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "darkMode", isDark.ToString().ToLower());
        }
        catch
        {
            // Handle case where localStorage is not available
        }

        OnThemeChanged?.Invoke();
    }
}