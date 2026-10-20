using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public partial class StashViewModel : ObservableObject
{
    private readonly IGitRepositoryReader _reader;
    private readonly StashService _stashService;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;

    public ObservableCollection<GitStash> Stashes { get; } = new();

    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }

    /// <summary>Asks for one line of text (title, prompt, initial value); null when cancelled.</summary>
    public Func<string, string, string?, Task<string?>>? RequestPrompt { get; set; }

    /// <summary>
    /// The commit summary typed in the working-directory panel. Like GitKraken, the toolbar Stash
    /// uses it as the stash message; <see cref="StashMessageUsed"/> then clears it.
    /// </summary>
    public Func<string?>? PendingMessage { get; set; }

    public event Action? StashMessageUsed;

    /// <summary>Raised when a stash is selected in the panel, to show its changes like a commit.</summary>
    public event Action<GitStash>? StashSelected;

    [ObservableProperty]
    private GitStash? selectedStash;

    partial void OnSelectedStashChanged(GitStash? value)
    {
        if (value is not null)
        {
            StashSelected?.Invoke(value);
        }
    }

    private readonly NotificationService _notifications;

    public StashViewModel(IGitRepositoryReader reader, StashService stashService, RepositoryChangeNotifier changeNotifier, CommandErrorHandler errorHandler, NotificationService notifications)
    {
        _notifications = notifications;
        _reader = reader;
        _stashService = stashService;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
    }

    public void Clear()
    {
        _repositoryPath = null;
        Stashes.Clear();
    }

    public async Task LoadAsync(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
        var stashes = await _reader.GetStashesAsync(repositoryPath);
        // Reloading must not re-raise StashSelected for the list's previous selection.
        SelectedStash = null;
        Stashes.Clear();
        foreach (var stash in stashes)
        {
            Stashes.Add(stash);
        }
    }

    [RelayCommand]
    private Task SaveAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        var message = PendingMessage?.Invoke();
        if (string.IsNullOrWhiteSpace(message))
        {
            await _stashService.SaveAsync(_repositoryPath, message: null);
        }
        else
        {
            await _stashService.SaveAsync(_repositoryPath, message.Trim());
            StashMessageUsed?.Invoke();
        }

        _notifications.Show("Stash saved");
        _changeNotifier.NotifyChanged();
    }, "save stash");

    [RelayCommand]
    private Task SaveWithMessageAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null || RequestPrompt is null)
        {
            return;
        }

        var message = await RequestPrompt("Stash changes", "Stash message:", PendingMessage?.Invoke());
        if (message is null)
        {
            return;
        }

        await _stashService.SaveAsync(_repositoryPath, string.IsNullOrWhiteSpace(message) ? null : message.Trim());
        _changeNotifier.NotifyChanged();
    }, "save stash");

    [RelayCommand]
    private Task ApplyAsync(GitStash stash) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        await _stashService.ApplyAsync(_repositoryPath, stash.Index);
        _notifications.Show("Stash applied");
        _changeNotifier.NotifyChanged();
    }, "apply stash");

    [RelayCommand]
    private Task PopAsync(GitStash stash) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        await _stashService.PopAsync(_repositoryPath, stash.Index);
        _notifications.Show("Stash popped");
        _changeNotifier.NotifyChanged();
    }, "pop stash");

    // Toolbar "Pop" acts on the most recent stash (stash@{0}), like `git stash pop` without an argument.
    [RelayCommand]
    private Task PopLatestAsync() => Stashes.Count == 0 ? Task.CompletedTask : PopAsync(Stashes.MinBy(s => s.Index)!);

    [RelayCommand]
    private Task DropAsync(GitStash stash) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        var confirmed = RequestConfirmation is null
            || await RequestConfirmation.Invoke("Drop stash", $"Drop stash '{stash.Message}'? This cannot be undone.");

        if (!confirmed)
        {
            return;
        }

        await _stashService.DropAsync(_repositoryPath, stash.Index);
        _changeNotifier.NotifyChanged();
    }, "drop stash");
}
