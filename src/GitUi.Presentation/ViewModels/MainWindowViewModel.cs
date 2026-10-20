using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Exceptions;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly RepositoryService _repositoryService;
    private readonly RecentRepositoriesService _recentRepositoriesService;
    private readonly ICredentialResolver _credentialResolver;
    private readonly ISettingsStore _settingsStore;
    private readonly ThemeService _themeService;
    private readonly CommandErrorHandler _errorHandler;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepositoryName))]
    private string? currentRepositoryPath;

    [ObservableProperty]
    private bool isBusy;

    /// <summary>
    /// The new-tab page (open / clone / init) replaces the repository view. Shown at startup until a
    /// repository opens, when the last tab closes, and from the "+" tab button.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloseHome))]
    private bool isHomeVisible = true;

    public bool CanCloseHome => IsHomeVisible && CurrentRepositoryPath is not null;

    [ObservableProperty]
    private string? statusMessage;

    public string RepositoryName => string.IsNullOrEmpty(CurrentRepositoryPath)
        ? "No repository"
        : Path.GetFileName(CurrentRepositoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public ObservableCollection<string> RecentRepositories { get; } = new();

    public ObservableCollection<RepositoryTab> Tabs { get; } = new();

    public bool IsDarkTheme => _themeService.IsDark;

    public string ThemeToggleLabel => _themeService.IsDark ? "Light" : "Dark";

    public string ThemeToggleIcon => _themeService.IsDark ? "☀" : "☾";

    public Func<Task<string?>>? RequestFolderPick { get; set; }
    public Func<string, Task>? RequestErrorDialog { get; set; }
    public event Action<string>? RepositoryOpened;
    public event Action? RepositoryClosed;

    /// <summary>Asks the view to show the new-tab page on a given section (open / clone / init).</summary>
    public event Action<HomeSection>? HomeSectionRequested;

    // Most recently closed tab last, for "Reopen Closed Tab" (Ctrl+Shift+T).
    private readonly Stack<string> _closedTabs = new();

    public MainWindowViewModel(
        RepositoryService repositoryService,
        RecentRepositoriesService recentRepositoriesService,
        ICredentialResolver credentialResolver,
        ISettingsStore settingsStore,
        ThemeService themeService,
        CommandErrorHandler errorHandler)
    {
        _repositoryService = repositoryService;
        _recentRepositoriesService = recentRepositoriesService;
        _credentialResolver = credentialResolver;
        _settingsStore = settingsStore;
        _themeService = themeService;
        _errorHandler = errorHandler;
        _themeService.ThemeChanged += () =>
        {
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(ThemeToggleLabel));
            OnPropertyChanged(nameof(ThemeToggleIcon));
        };
    }

    public async Task InitializeAsync()
    {
        var recents = await _recentRepositoriesService.GetRecentAsync();
        RecentRepositories.Clear();
        foreach (var path in recents)
        {
            RecentRepositories.Add(path);
        }
    }

    /// <summary>Reopens the tabs of the previous session and activates the one that was active.</summary>
    public async Task RestoreSessionAsync()
    {
        await InitializeAsync();

        var settings = await _settingsStore.LoadAsync();
        foreach (var path in settings.OpenRepositoryPaths ?? Array.Empty<string>())
        {
            if (Directory.Exists(path) && !Tabs.Any(t => SamePath(t.Path, path)))
            {
                Tabs.Add(new RepositoryTab(path));
            }
        }

        var active = Tabs.FirstOrDefault(t => SamePath(t.Path, settings.ActiveRepositoryPath)) ?? Tabs.FirstOrDefault();
        if (active is not null)
        {
            await OpenRepositoryPathAsync(active.Path);
        }
    }

    [RelayCommand]
    private Task OpenRepositoryAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (RequestFolderPick is null)
        {
            return;
        }

        var path = await RequestFolderPick.Invoke();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // OpenRepositoryPathAsync keeps its own, more specific handling for a corrupt repository;
        // this guard covers everything else on the open path (credential helper, recents store, IO).
        await OpenRepositoryPathAsync(path);
    }, "open repository");

    [RelayCommand]
    private Task ActivateTabAsync(RepositoryTab tab) => _errorHandler.RunGuardedAsync(
        () => tab.IsActive ? Task.CompletedTask : OpenRepositoryPathAsync(tab.Path),
        "switch repository tab");

    [RelayCommand]
    private Task CloseTabAsync(RepositoryTab tab) => _errorHandler.RunGuardedAsync(async () =>
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        Tabs.RemoveAt(index);
        _closedTabs.Push(tab.Path);
        ReopenClosedTabCommand.NotifyCanExecuteChanged();
        if (tab.IsActive)
        {
            if (Tabs.Count > 0)
            {
                await OpenRepositoryPathAsync(Tabs[Math.Min(index, Tabs.Count - 1)].Path);
                return;
            }

            CurrentRepositoryPath = null;
            StatusMessage = null;
            IsHomeVisible = true;
            RepositoryClosed?.Invoke();
        }

        await SaveTabsAsync();
    }, "close repository tab");

    /// <summary>Closes every tab but <paramref name="keep"/> (each can be reopened with Ctrl+Shift+T).</summary>
    [RelayCommand]
    private Task CloseOtherTabsAsync(RepositoryTab keep) => _errorHandler.RunGuardedAsync(async () =>
    {
        foreach (var tab in Tabs.Where(t => !ReferenceEquals(t, keep)).ToList())
        {
            Tabs.Remove(tab);
            _closedTabs.Push(tab.Path);
        }

        ReopenClosedTabCommand.NotifyCanExecuteChanged();
        if (!keep.IsActive)
        {
            await OpenRepositoryPathAsync(keep.Path);
        }

        await SaveTabsAsync();
    }, "close other tabs");

    [RelayCommand]
    private void ShowHome() => IsHomeVisible = true;

    /// <summary>New tab on the Open / Clone / Init section — what File ▸ Open, Clone and Init Repo do.</summary>
    [RelayCommand]
    private void ShowHomeSection(HomeSection section)
    {
        IsHomeVisible = true;
        HomeSectionRequested?.Invoke(section);
    }

    [RelayCommand]
    private Task OpenRecentAsync(string path) => _errorHandler.RunGuardedAsync(() => OpenRepositoryPathAsync(path), "open recent repository");

    [RelayCommand]
    private Task CloseCurrentTabAsync() =>
        Tabs.FirstOrDefault(t => t.IsActive) is { } active ? CloseTabAsync(active) : Task.CompletedTask;

    private bool CanReopenClosedTab() => _closedTabs.Count > 0;

    [RelayCommand(CanExecute = nameof(CanReopenClosedTab))]
    private Task ReopenClosedTabAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        while (_closedTabs.TryPop(out var path))
        {
            if (Directory.Exists(path))
            {
                await OpenRepositoryPathAsync(path);
                break;
            }
        }

        ReopenClosedTabCommand.NotifyCanExecuteChanged();
    }, "reopen closed tab");

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: activate the next / previous tab, wrapping around.</summary>
    [RelayCommand]
    private Task CycleTabAsync(int step)
    {
        if (Tabs.Count < 2)
        {
            return Task.CompletedTask;
        }

        var current = Tabs.IndexOf(Tabs.FirstOrDefault(t => t.IsActive)!);
        var next = ((current < 0 ? 0 : current) + step + Tabs.Count) % Tabs.Count;
        return ActivateTabAsync(Tabs[next]);
    }

    /// <summary>Ctrl+1 … Ctrl+9 (Ctrl+9 = last tab, like browsers and GitKraken).</summary>
    [RelayCommand]
    private Task SelectTabAsync(int number)
    {
        if (Tabs.Count == 0)
        {
            return Task.CompletedTask;
        }

        var index = number >= 9 ? Tabs.Count - 1 : number - 1;
        return index >= 0 && index < Tabs.Count ? ActivateTabAsync(Tabs[index]) : Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenInFileExplorer()
    {
        if (CurrentRepositoryPath is { } path)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    /// <summary>Opens an external terminal in the repository: Windows Terminal when installed, else PowerShell.</summary>
    [RelayCommand]
    private void OpenExternalTerminal()
    {
        if (CurrentRepositoryPath is not { } path)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wt.exe", $"-d \"{path}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe") { WorkingDirectory = path, UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void CloseHome()
    {
        if (CurrentRepositoryPath is not null)
        {
            IsHomeVisible = false;
        }
    }

    [RelayCommand]
    private Task ToggleThemeAsync() => _errorHandler.RunGuardedAsync(
        () => _themeService.SetDarkAsync(!_themeService.IsDark),
        "switch theme");

    public async Task OpenRepositoryPathAsync(string path)
    {
        IsBusy = true;
        try
        {
            await _repositoryService.OpenAsync(path);

            var tab = Tabs.FirstOrDefault(t => SamePath(t.Path, path));
            if (tab is null)
            {
                tab = new RepositoryTab(path);
                Tabs.Add(tab);
            }

            foreach (var other in Tabs)
            {
                other.IsActive = ReferenceEquals(other, tab);
            }

            CurrentRepositoryPath = tab.Path;
            IsHomeVisible = false;
            OnPropertyChanged(nameof(CanCloseHome));
            StatusMessage = $"Opened {tab.Path}";
            RepositoryOpened?.Invoke(tab.Path);
            await SaveTabsAsync();
            await _recentRepositoriesService.AddRecentAsync(tab.Path);
            await _credentialResolver.EnsureCredentialHelperConfiguredAsync(tab.Path);
            await InitializeAsync();
        }
        catch (GitRepositoryCorruptException ex)
        {
            if (RequestErrorDialog is not null)
            {
                await RequestErrorDialog.Invoke(ex.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task SaveTabsAsync()
    {
        var openPaths = Tabs.Select(t => t.Path).ToList();
        var activePath = CurrentRepositoryPath;
        return _settingsStore.UpdateAsync(settings => settings with
        {
            OpenRepositoryPaths = openPaths,
            ActiveRepositoryPath = activePath,
        });
    }

    private static bool SamePath(string? left, string? right) =>
        left is not null && right is not null &&
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}
