using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitUi.Presentation.Controls;

namespace GitUi.Presentation.Views;

public partial class SplashWindow : Window
{
    /// <summary>How long to wait for the app before giving up on the splash and showing it anyway.</summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    private readonly System.Diagnostics.Stopwatch _shown = new();

    public SplashWindow()
    {
        InitializeComponent();
        Opened += (_, _) => _shown.Start();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Completes once the animation has played its minimum duration and <paramref name="appReady"/>
    /// has finished (or timed out). Past the minimum, the bar turns indeterminate while waiting.
    /// </summary>
    public async Task WaitUntilDoneAsync(Task appReady)
    {
        // The minimum counts from when the splash appeared, not from when this was called.
        var remaining = SplashAnimation.DurationMs - _shown.Elapsed.TotalMilliseconds;
        var minimum = Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, remaining)));
        var ready = Task.WhenAny(appReady, Task.Delay(ReadyTimeout));

        await minimum;
        if (!ready.IsCompleted)
        {
            this.FindControl<SplashAnimation>("Animation")!.IsWaiting = true;
        }

        await ready;
    }
}
