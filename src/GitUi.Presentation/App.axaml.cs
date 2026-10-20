using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitUi.Infrastructure.DependencyInjection;
using GitUi.Infrastructure.Git;
using GitUi.Infrastructure.Logging;
using GitUi.Presentation.Services;
using GitUi.Presentation.ViewModels;
using GitUi.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GitUi.Presentation;

public partial class App : Avalonia.Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        // GITUI_DATA_DIR redirects settings and logs, so a second instance (tests, demos) never
        // overwrites the user's open tabs and preferences.
        var gitUiDataPath = Environment.GetEnvironmentVariable("GITUI_DATA_DIR") is { Length: > 0 } dataDir
            ? dataDir
            : Path.Combine(appDataPath, "GitUi");
        var settingsFilePath = Path.Combine(gitUiDataPath, "settings.json");
        var logDirectory = Path.Combine(gitUiDataPath, "logs");

        var services = new ServiceCollection();
        var loggerFactory = SerilogConfigurator.CreateLoggerFactory(logDirectory);
        services.AddSingleton(loggerFactory);
        services.AddLogging(builder => builder.AddProvider(new LoggerFactoryProviderAdapter(loggerFactory)));
        services.AddGitUiInfrastructure(settingsFilePath);
        services.AddSingleton<DialogService>();
        services.AddSingleton<CommandErrorHandler>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<RepositoryChangeNotifier>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<GitUi.Presentation.ViewModels.HistoryViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.RefActionsViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.MergeToolViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.HomeViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.ConflictDetectionViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.CommitDetailsViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.FileHistoryViewModel>();
        services.AddTransient<GitUi.Presentation.ViewModels.PreferencesViewModel>();
        services.AddTransient<GitUi.Presentation.ViewModels.AccountsViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.PullRequestsViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.BisectViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.GitFlowViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.LfsViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.ProfilesViewModel>();
        services.AddTransient<GitUi.Presentation.ViewModels.ReflogViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.InteractiveRebaseViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.DiffSettings>();
        services.AddSingleton<GitUi.Presentation.ViewModels.CommandPaletteViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.CommitGraphViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.StagingViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.BranchViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.StashViewModel>();
        services.AddSingleton<GitUi.Presentation.ViewModels.RemoteActionsViewModel>();
        services.AddSingleton<GitUi.Infrastructure.Processes.ShellCommandRunner>();
        services.AddSingleton<GitUi.Presentation.ViewModels.CommandViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var availabilityChecker = Services.GetRequiredService<GitAvailabilityChecker>();
            var isGitAvailable = availabilityChecker.IsGitAvailableAsync().GetAwaiter().GetResult();

            if (!isGitAvailable)
            {
                var window = new Window { Width = 420, Height = 160 };
                window.Content = new TextBlock
                {
                    Margin = new Thickness(16),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Text = "git was not found on the PATH. Install Git and restart Git UI.",
                };
                desktop.MainWindow = window;
            }
            else
            {
                // Apply the saved theme (dark by default) before the first frame. The settings are
                // read off the UI thread so blocking on them cannot deadlock the dispatcher.
                var settingsStore = Services.GetRequiredService<GitUi.Application.Abstractions.ISettingsStore>();
                var settings = Task.Run(() => settingsStore.LoadAsync()).GetAwaiter().GetResult();
                Services.GetRequiredService<ThemeService>().ApplyFromSettings(settings);

                // Loading screen first: the main window is built (and restores the open tabs) behind
                // it, then replaces it once the animation's minimum duration has passed.
                var splash = new SplashWindow();
                desktop.MainWindow = splash;
                _ = ShowMainWindowAfterSplashAsync(desktop, splash);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowMainWindowAfterSplashAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        // Build the (heavy) main window only once the splash has painted, so the loading screen
        // appears immediately instead of after the main window's XAML has loaded.
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);

        var mainWindow = new MainWindow();

        // Set before the DataContext so the view wiring (and the fire-and-forget loads it
        // starts) already has a window to raise error dialogs on.
        Services!.GetRequiredService<DialogService>().OwnerWindow = mainWindow;
        mainWindow.DataContext = Services!.GetRequiredService<MainWindowViewModel>();

        try
        {
            await splash.WaitUntilDoneAsync(mainWindow.StartupTask);
        }
        finally
        {
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();
        }
    }
}
