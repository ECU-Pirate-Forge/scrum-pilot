using Microsoft.JSInterop;

namespace ScrumPilot.Web.Services;

public class ThemeService : IThemeService
{
    private bool _isDarkMode = false;
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
            // Try to load saved theme preference from localStorage
            var savedTheme = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "darkMode");
            _isDarkMode = savedTheme == "true";
        }
        catch
        {
            // If localStorage is not available (SSR), default to false
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