namespace ScrumPilot.Web.Services;

public interface IThemeService
{
    bool IsDarkMode { get; }
    event Action OnThemeChanged;
    Task InitializeAsync();
    void ToggleDarkMode();
    Task SetDarkMode(bool isDark);
}