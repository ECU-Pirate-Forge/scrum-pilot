using Microsoft.FluentUI.AspNetCore.Components;

namespace ScrumPilot.FluentWeb.Services;

/// <summary>
/// Thin wrapper around the Fluent toast API so pages can raise notifications without awaiting dismissal.
/// </summary>
public interface IAppNotificationService
{
    void ShowSuccess(string message, string? title = null);
    void ShowInfo(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowError(string message, string? title = null);
}

public sealed class AppNotificationService(INotificationService notifications, ILogger<AppNotificationService> logger) : IAppNotificationService
{
    public void ShowSuccess(string message, string? title = null)
        => Fire(notifications.ShowSuccessToastAsync(title ?? "Success", message), message);

    public void ShowInfo(string message, string? title = null)
        => Fire(notifications.ShowInfoToastAsync(title ?? "Information", message), message);

    public void ShowWarning(string message, string? title = null)
        => Fire(notifications.ShowWarningToastAsync(title ?? "Warning", message), message);

    public void ShowError(string message, string? title = null)
        => Fire(notifications.ShowErrorToastAsync(title ?? "Error", message), message);

    // Toast tasks complete only when the toast closes, so they are observed rather than awaited.
    private void Fire(Task task, string message)
    {
        _ = task.ContinueWith(
            t => logger.LogWarning(t.Exception, "Failed to display notification: {Message}", message),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
