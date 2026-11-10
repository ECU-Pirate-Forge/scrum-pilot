using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace ScrumPilot.FluentWeb.Services;

/// <summary>
/// Confirmation and alert dialogs built on the Fluent dialog service.
/// </summary>
public interface IAppDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Confirm", string cancelLabel = "Cancel", bool destructive = false);

    Task ShowErrorAsync(string message, string title = "Error");
}

public sealed class AppDialogService(IDialogService dialogs) : IAppDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Confirm", string cancelLabel = "Cancel", bool destructive = false)
    {
        var result = await dialogs.ShowMessageBoxAsync(new MessageBoxOptions
        {
            Title = title,
            Message = message,
            PrimaryButton = confirmLabel,
            SecondaryButton = cancelLabel,
            Icon = destructive ? new Icons.Regular.Size24.Warning() : new Icons.Regular.Size24.QuestionCircle(),
            IconColor = destructive ? Color.Error : Color.Default,
        });

        return !result.Cancelled;
    }

    public async Task ShowErrorAsync(string message, string title = "Error")
        => await dialogs.ShowErrorAsync(message, title);
}

