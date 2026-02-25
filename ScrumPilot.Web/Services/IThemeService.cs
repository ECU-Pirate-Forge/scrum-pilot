namespace ScrumPilot.Web.Services;

public interface IThemeService
{
    bool IsDarkMode { get; }
    event Action OnThemeChanged;
    void ToggleDarkMode();
    void SetDarkMode(bool isDark);
}