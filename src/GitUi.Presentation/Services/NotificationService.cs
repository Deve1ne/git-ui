using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace GitUi.Presentation.Services;

/// <summary>One toast: a short confirmation that an operation went through.</summary>
public sealed record Toast(string Message);

/// <summary>
/// GitKraken-style toasts in the bottom-right corner ("Pushed main to origin", "Stash saved"...).
/// Each one disappears by itself after a few seconds; at most three show at once.
/// </summary>
public sealed class NotificationService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);

    public ObservableCollection<Toast> Toasts { get; } = new();

    public void Show(string message) => Dispatcher.UIThread.Post(() =>
    {
        var toast = new Toast(message);
        Toasts.Add(toast);
        while (Toasts.Count > 3)
        {
            Toasts.RemoveAt(0);
        }

        DispatcherTimer.RunOnce(() => Toasts.Remove(toast), Lifetime);
    });
}
