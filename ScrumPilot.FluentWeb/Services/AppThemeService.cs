using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using ScrumPilot.Shared.Models;

namespace ScrumPilot.FluentWeb.Services;

/// <summary>
/// Applies the user's System/Light/Dark preference to Fluent design tokens and to the
/// <c>data-theme</c> attribute on <c>&lt;html&gt;</c> (used by app CSS and charts).
/// </summary>
public sealed class AppThemeService(IJSRuntime js, IThemeService fluentTheme, ILogger<AppThemeService> logger)
{
    public UiPreference Preference { get; private set; } = UiPreference.System;

    public bool IsDark { get; private set; }

    public event Action? OnChange;

    public async Task ApplyAsync(UiPreference preference)
    {
        Preference = preference;
        var mode = preference switch
        {
            UiPreference.Dark => ThemeMode.Dark,
            UiPreference.Light => ThemeMode.Light,
            _ => ThemeMode.System,
        };

        try
        {
            await fluentTheme.SetThemeAsync(mode);
            IsDark = await js.InvokeAsync<bool>("scrumPilotTheme.apply", preference.ToString().ToLowerInvariant());
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Unable to apply theme {Preference}", preference);
        }

        OnChange?.Invoke();
    }
}
