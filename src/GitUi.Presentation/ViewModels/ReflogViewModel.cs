using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>One reflog line in the Reflog window.</summary>
public sealed record ReflogRow(ReflogEntry Entry)
{
    public string When => Entry.Date?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;
}

/// <summary>
/// Reflog window: every position HEAD had, newest first. From an entry: show the commit, create a
/// branch on it (to get back lost work), check it out, or reset the current branch to it.
/// </summary>
public partial class ReflogViewModel : ObservableObject
{
    private readonly ReflogService _reflog;
    private readonly IGitRepositoryWriter _writer;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private readonly NotificationService _notifications;
    private IReadOnlyList<ReflogEntry> _entries = Array.Empty<ReflogEntry>();
    private string? _repositoryPath;

    public ReflogViewModel(
        ReflogService reflog,
        IGitRepositoryWriter writer,
        RepositoryChangeNotifier changeNotifier,
        CommandErrorHandler errorHandler,
        NotificationService notifications)
    {
        _reflog = reflog;
        _writer = writer;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
        _notifications = notifications;
    }

    public ObservableCollection<ReflogRow> Rows { get; } = new();

    [ObservableProperty]
    private string filter = string.Empty;

    partial void OnFilterChanged(string value) => ApplyFilter();

    [ObservableProperty]
    private ReflogRow? selectedRow;

    [ObservableProperty]
    private string summary = string.Empty;

    public Func<string, string, string?, Task<string?>>? RequestInput { get; set; }
    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }
    public Func<string, Task>? RequestClipboard { get; set; }

    /// <summary>Raised with a SHA to show that commit (the window closes).</summary>
    public event Action<string>? ShowCommitRequested;

    public async Task LoadAsync(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
        _entries = await _reflog.GetAsync(repositoryPath);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        Rows.Clear();
        foreach (var entry in _entries.Where(e => filter.Length == 0
            || e.Message.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || e.Sha.StartsWith(filter, StringComparison.OrdinalIgnoreCase)))
        {
            Rows.Add(new ReflogRow(entry));
        }

        Summary = filter.Length == 0 ? $"{_entries.Count} entries" : $"{Rows.Count} of {_entries.Count} entries";
    }

    [RelayCommand]
    private void ShowCommit(ReflogRow? row)
    {
        if ((row ?? SelectedRow) is { } target)
        {
            ShowCommitRequested?.Invoke(target.Entry.Sha);
        }
    }

    [RelayCommand]
    private Task CopyShaAsync(ReflogRow row) => RequestClipboard?.Invoke(row.Entry.Sha) ?? Task.CompletedTask;

    [RelayCommand]
    private Task CreateBranchAsync(ReflogRow row) => RunAsync(async path =>
    {
        if (RequestInput is null || await RequestInput("Create branch", $"New branch at {row.Entry.ShortSha} ({row.Entry.Message}):", null) is not { } name
            || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _writer.CreateBranchAsync(path, name.Trim(), row.Entry.Sha);
        _notifications.Show($"Created branch {name.Trim()} at {row.Entry.ShortSha}");
    }, "create branch");

    [RelayCommand]
    private Task CheckoutAsync(ReflogRow row) => RunAsync(async path =>
    {
        await _writer.CheckoutCommitAsync(path, row.Entry.Sha);
        _notifications.Show($"Checked out {row.Entry.ShortSha} (detached HEAD)");
    }, "checkout commit");

    [RelayCommand]
    private Task ResetSoftAsync(ReflogRow row) => ResetAsync(row, GitResetMode.Soft);

    [RelayCommand]
    private Task ResetMixedAsync(ReflogRow row) => ResetAsync(row, GitResetMode.Mixed);

    [RelayCommand]
    private Task ResetHardAsync(ReflogRow row) => ResetAsync(row, GitResetMode.Hard);

    private Task ResetAsync(ReflogRow row, GitResetMode mode) => RunAsync(async path =>
    {
        if (mode == GitResetMode.Hard && RequestConfirmation is not null
            && !await RequestConfirmation("Reset (hard)", $"Reset the current branch to {row.Entry.ShortSha} and discard every uncommitted change?"))
        {
            return;
        }

        await _writer.ResetAsync(path, row.Entry.Sha, mode);
        _notifications.Show($"Reset ({mode.ToString().ToLowerInvariant()}) to {row.Entry.ShortSha}");
    }, "reset");

    private Task RunAsync(Func<string, Task> action, string operation) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path)
        {
            return;
        }

        try
        {
            await action(path);
        }
        finally
        {
            _changeNotifier.NotifyChanged();
            await LoadAsync(path);
        }
    }, operation);
}
