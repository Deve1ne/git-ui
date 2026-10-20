using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// The bisect banner over the graph: what to do next (mark a bad / good commit), the commit being
/// tested with Good / Bad / Skip, the first bad commit once found, and Reset to end the session.
/// Commits are marked from the graph's context menu (Bisect &gt;) or from the banner for the one checked out.
/// </summary>
public partial class BisectViewModel : ObservableObject
{
    private readonly BisectService _bisect;
    private readonly IGitRepositoryReader _reader;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private readonly NotificationService _notifications;
    private string? _repositoryPath;
    private BisectState _state = BisectState.Inactive;

    public BisectViewModel(
        BisectService bisect,
        IGitRepositoryReader reader,
        RepositoryChangeNotifier changeNotifier,
        CommandErrorHandler errorHandler,
        NotificationService notifications)
    {
        _bisect = bisect;
        _reader = reader;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
        _notifications = notifications;
    }

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private string headline = string.Empty;

    [ObservableProperty]
    private string? detail;

    /// <summary>Good / Bad / Skip apply to the checked-out commit: shown while testing.</summary>
    [ObservableProperty]
    private bool canMarkCurrent;

    [ObservableProperty]
    private bool isFinished;

    /// <summary>Raised to select a commit in the graph (the first bad one).</summary>
    public event Action<string>? SelectCommitRequested;

    public void Clear()
    {
        _repositoryPath = null;
        _state = BisectState.Inactive;
        IsActive = false;
    }

    public async Task LoadAsync(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
        var state = await _bisect.GetStateAsync(repositoryPath);
        _state = state;
        IsActive = state.IsActive;
        if (!state.IsActive)
        {
            return;
        }

        IsFinished = state.IsFinished;
        CanMarkCurrent = !state.IsFinished && state.Current is not null;
        if (state.FirstBad is { } firstBad)
        {
            Headline = $"Bisect found the first bad commit: {Short(firstBad)} {await SummaryAsync(firstBad)}";
            Detail = "Reset ends the session and goes back to where you started.";
        }
        else if (state.Bad is null)
        {
            Headline = "Bisecting: mark a commit that has the bug as bad.";
            Detail = "Right-click a commit in the graph > Bisect, or mark the checked-out one below.";
        }
        else if (state.Good.Count == 0)
        {
            Headline = "Bisecting: now mark a commit from before the bug as good.";
            Detail = "Right-click an older commit in the graph > Bisect > Mark as good.";
        }
        else
        {
            Headline = $"Bisecting: test {Short(state.Current)} {await SummaryAsync(state.Current)}";
            Detail = $"{state.RevisionsLeft} revision(s) left after this one (roughly {state.StepsLeft} step(s)). Does the checked-out commit have the bug?";
        }
    }

    public Task MarkAsync(string sha, BisectMark mark) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path)
        {
            return;
        }

        try
        {
            _notifications.Show(await _bisect.MarkAsync(path, sha, mark));
        }
        finally
        {
            _changeNotifier.NotifyChanged();
        }
    }, "bisect");

    [RelayCommand]
    private Task GoodAsync() => _state.Current is { } sha ? MarkAsync(sha, BisectMark.Good) : Task.CompletedTask;

    [RelayCommand]
    private Task BadAsync() => _state.Current is { } sha ? MarkAsync(sha, BisectMark.Bad) : Task.CompletedTask;

    [RelayCommand]
    private Task SkipAsync() => _state.Current is { } sha ? MarkAsync(sha, BisectMark.Skip) : Task.CompletedTask;

    [RelayCommand]
    private void ShowFirstBad()
    {
        if (_state.FirstBad is { } sha)
        {
            SelectCommitRequested?.Invoke(sha);
        }
    }

    [RelayCommand]
    private Task ResetAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path)
        {
            return;
        }

        var firstBad = _state.FirstBad;
        try
        {
            await _bisect.ResetAsync(path);
            _notifications.Show(firstBad is null ? "Bisect ended" : $"Bisect ended — first bad commit {Short(firstBad)}");
        }
        finally
        {
            _changeNotifier.NotifyChanged();
        }

        if (firstBad is not null)
        {
            SelectCommitRequested?.Invoke(firstBad);
        }
    }, "end bisect");

    private async Task<string> SummaryAsync(string? sha)
    {
        if (sha is null || _repositoryPath is not { } path)
        {
            return string.Empty;
        }

        try
        {
            return $"\"{(await _reader.GetCommitDetailsAsync(path, sha)).Summary}\"";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string Short(string? sha) => sha is null ? string.Empty : sha[..Math.Min(7, sha.Length)];
}
