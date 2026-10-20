using Avalonia.Controls;
using Avalonia.Threading;
using GitUi.Presentation.Views;

namespace GitUi.Presentation.Services;

public sealed class DialogService
{
    /// <summary>
    /// Window that owns dialogs raised from contexts that have no window reference of their own
    /// (panel view models). Assigned once by <see cref="App"/> right after the main window is built.
    /// </summary>
    public Window? OwnerWindow { get; set; }

    public Task<bool> ConfirmDestructiveActionAsync(Window owner, string title, string message)
    {
        var dialog = new ConfirmationDialog(title, message);
        return dialog.ShowDialog<bool>(owner);
    }

    /// <summary>Asks for a single line of text; null when the user cancels.</summary>
    public Task<string?> PromptAsync(Window owner, string title, string prompt, string? initialValue = null)
    {
        var dialog = new InputDialog(title, prompt, initialValue);
        return dialog.ShowDialog<string?>(owner);
    }

    public Task ShowErrorAsync(Window owner, string message, string logFilePath)
    {
        var dialog = new ErrorDialog(message, logFilePath);
        return dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Shows an error dialog owned by <see cref="OwnerWindow"/>. This sits on the error-reporting
    /// path, so it never throws: with no owner window (or no UI thread) it simply does nothing.
    /// </summary>
    public Task ShowErrorAsync(string message, string logFilePath)
    {
        var owner = OwnerWindow;
        if (owner is null)
        {
            return Task.CompletedTask;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowErrorAsync(owner, message, logFilePath);
        }

        return Dispatcher.UIThread.InvokeAsync(() => ShowErrorAsync(owner, message, logFilePath));
    }
}
