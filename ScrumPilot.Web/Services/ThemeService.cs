namespace ScrumPilot.Web.Services;

public class ThemeService : IThemeService
{
    private bool _isDarkMode;
    
    public event Action? OnThemeChanged;

    public bool IsDarkMode => _isDarkMode;

    public void ToggleDarkMode()
    {
        SetDarkMode(!_isDarkMode);
    }

    public void SetDarkMode(bool isDark)
    {
        _isDarkMode = isDark;
        OnThemeChanged?.Invoke();
    }
}