using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// LFS section of the ref panel (shown when git-lfs is installed): the tracked patterns, track /
/// untrack, and pull / fetch / prune of the large files.
/// </summary>
public partial class LfsViewModel : ObservableObject
{
    private readonly LfsService _lfs;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private readonly NotificationService _notifications;
    private string? _repositoryPath;

    public LfsViewModel(LfsService lfs, RepositoryChangeNotifier changeNotifier, CommandErrorHandler errorHandler, NotificationService notifications)
    {
        _lfs = lfs;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
        _notifications = notifications;
    }

    public ObservableCollection<string> Patterns { get; } = new();

    [ObservableProperty]
    private bool isAvailable;

    [ObservableProperty]
    private bool isUsed;

    [ObservableProperty]
    private string header = "LFS";

    [ObservableProperty]
    private string? fileSummary;

    [ObservableProperty]
    private bool isBusy;

    public Func<string, string, string?, Task<string?>>? RequestInput { get; set; }
    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }

    public void Clear()
    {
        _repositoryPath = null;
        IsAvailable = false;
        Patterns.Clear();
    }

    public async Task LoadAsync(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
        var status = await _lfs.GetStatusAsync(repositoryPath);
        IsAvailable = status.IsInstalled;
        IsUsed = status.IsUsed;
        Patterns.Clear();
        foreach (var pattern in status.TrackedPatterns)
        {
            Patterns.Add(pattern);
        }

        Header = status.IsUsed ? $"LFS ({status.TrackedPatterns.Count})" : "LFS";
        FileSummary = status.FileCount > 0 ? $"{status.FileCount} file(s) stored in LFS" : null;
    }

    [RelayCommand]
    private Task TrackAsync() => RunAsync(async path =>
    {
        var answer = RequestInput is null ? null : await RequestInput("Track with LFS", "File pattern to store in LFS (e.g. *.psd, assets/**/*.bin):", null);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        await _lfs.TrackAsync(path, answer);
        _notifications.Show($"LFS now tracks {answer.Trim()} (.gitattributes staged)");
    }, "LFS track");

    [RelayCommand]
    private Task UntrackAsync(string pattern) => RunAsync(async path =>
    {
        if (RequestConfirmation is not null
            && !await RequestConfirmation("Untrack with LFS", $"Stop storing {pattern} in LFS? Files already committed stay in LFS; .gitattributes is updated and staged."))
        {
            return;
        }

        await _lfs.UntrackAsync(path, pattern);
        _notifications.Show($"LFS no longer tracks {pattern}");
    }, "LFS untrack");

    [RelayCommand]
    private Task PullAsync() => RunAsync(async path =>
    {
        await _lfs.PullAsync(path);
        _notifications.Show("LFS files pulled");
    }, "LFS pull");

    [RelayCommand]
    private Task FetchAsync() => RunAsync(async path =>
    {
        await _lfs.FetchAsync(path);
        _notifications.Show("LFS files fetched");
    }, "LFS fetch");

    [RelayCommand]
    private Task PruneAsync() => RunAsync(async path =>
    {
        var output = await _lfs.PruneAsync(path);
        _notifications.Show(output.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "LFS pruned");
    }, "LFS prune");

    [RelayCommand]
    private Task InstallAsync() => RunAsync(async path =>
    {
        await _lfs.InstallAsync(path);
        _notifications.Show("LFS hooks installed in this repository");
    }, "LFS install");

    private Task RunAsync(Func<string, Task> action, string operation) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action(path);
        }
        finally
        {
            IsBusy = false;
            _changeNotifier.NotifyChanged();
        }
    }, operation);
}
