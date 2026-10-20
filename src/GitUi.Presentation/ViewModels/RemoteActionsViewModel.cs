using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public partial class RemoteActionsViewModel : ObservableObject
{
    private readonly RemoteService _remoteService;
    private readonly ISettingsStore _settingsStore;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;

    [ObservableProperty]
    private bool isBusy;

    // The toolbar Pull button runs this mode; picking another mode in its dropdown runs it and
    // makes it the default, like the pull-mode selector of most git clients.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PullModeLabel))]
    private GitPullMode pullMode = GitPullMode.Merge;

    public string PullModeLabel => PullMode switch
    {
        GitPullMode.FastForwardOnly => "Pull (fast-forward only)",
        GitPullMode.Rebase => "Pull (rebase)",
        _ => "Pull (fast-forward if possible)",
    };

    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }

    private readonly PushService _pushService;

    private readonly NotificationService _notifications;

    public RemoteActionsViewModel(RemoteService remoteService, PushService pushService, ISettingsStore settingsStore, RepositoryChangeNotifier changeNotifier, CommandErrorHandler errorHandler, NotificationService notifications)
    {
        _notifications = notifications;
        _remoteService = remoteService;
        _pushService = pushService;
        _settingsStore = settingsStore;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
    }

    public void SetRepositoryPath(string? repositoryPath) => _repositoryPath = repositoryPath;

    public async Task InitializeAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        if (Enum.TryParse<GitPullMode>(settings.PullMode, out var mode))
        {
            PullMode = mode;
        }

        var fetch = settings.FetchOptions ?? GitFetchOptions.Default;
        _loadingFetchOptions = true;
        FetchPrune = fetch.Prune;
        FetchPruneTags = fetch.PruneTags;
        FetchAllTags = fetch.Tags == GitFetchTags.All;
        FetchNoTags = fetch.Tags == GitFetchTags.None;
        FetchRecurseSubmodules = fetch.RecurseSubmodules;
        _loadingFetchOptions = false;
    }

    // ---- Fetch options (the Fetch button's dropdown), saved as soon as they change ----

    private bool _loadingFetchOptions;

    /// <summary>--prune: drop remote-tracking branches deleted on the remote.</summary>
    [ObservableProperty]
    private bool fetchPrune = true;

    /// <summary>--prune-tags: also drop local tags deleted on the remote.</summary>
    [ObservableProperty]
    private bool fetchPruneTags;

    /// <summary>--tags: every tag of the remote.</summary>
    [ObservableProperty]
    private bool fetchAllTags;

    /// <summary>--no-tags.</summary>
    [ObservableProperty]
    private bool fetchNoTags;

    /// <summary>--recurse-submodules=yes.</summary>
    [ObservableProperty]
    private bool fetchRecurseSubmodules;

    // Pruning tags only makes sense while pruning; --tags and --no-tags exclude each other.
    partial void OnFetchPruneChanged(bool value)
    {
        if (!value)
        {
            FetchPruneTags = false;
        }

        SaveFetchOptions();
    }

    partial void OnFetchPruneTagsChanged(bool value)
    {
        if (value)
        {
            FetchPrune = true;
        }

        SaveFetchOptions();
    }

    partial void OnFetchAllTagsChanged(bool value)
    {
        if (value)
        {
            FetchNoTags = false;
        }

        SaveFetchOptions();
    }

    partial void OnFetchNoTagsChanged(bool value)
    {
        if (value)
        {
            FetchAllTags = false;
        }

        SaveFetchOptions();
    }

    partial void OnFetchRecurseSubmodulesChanged(bool value) => SaveFetchOptions();

    public GitFetchOptions FetchOptions => new(
        FetchPrune,
        FetchPrune && FetchPruneTags,
        FetchAllTags ? GitFetchTags.All : FetchNoTags ? GitFetchTags.None : GitFetchTags.Default,
        FetchRecurseSubmodules);

    private void SaveFetchOptions()
    {
        if (!_loadingFetchOptions)
        {
            var options = FetchOptions;
            _ = _errorHandler.RunGuardedAsync(() => _remoteService.SaveFetchOptionsAsync(options), "save fetch options");
        }
    }

    [RelayCommand]
    private Task FetchAsync() => RunRemoteAsync(async () =>
    {
        var path = _repositoryPath!;
        var moved = await _remoteService.FetchWithOptionsAsync(path);
        if (moved.Count > 0 && await ConfirmMovedTagsAsync(moved))
        {
            await _remoteService.UpdateMovedTagsAsync(path);
            _notifications.Show($"Fetched all remotes; {moved.Count} moved tag(s) updated");
            return;
        }

        _notifications.Show(moved.Count > 0
            ? $"Fetched all remotes; {moved.Count} tag(s) moved on the server kept at your version"
            : FetchOptions.Prune ? "Fetched all remotes (pruned)" : "Fetched all remotes");
    }, "fetch");

    /// <summary>Tags moved (re-created) on the server: git keeps the local ones unless asked to overwrite them.</summary>
    public async Task<bool> ConfirmMovedTagsAsync(IReadOnlyList<string> tags) =>
        RequestConfirmation is not null && await RequestConfirmation(
            "Tags moved on the server",
            $"These tags point to another commit on the server than here: {string.Join(", ", tags.Take(10))}{(tags.Count > 10 ? ", …" : string.Empty)}.\n\n" +
            "Everything else was fetched. Replace your local tags with the server's version (git fetch --force)?");

    [RelayCommand]
    private Task PullAsync() => RunRemoteAsync(async () =>
    {
        await _remoteService.PullAsync(_repositoryPath!, PullMode);
        _notifications.Show(PullModeLabel.Replace("Pull", "Pulled"));
    }, "pull");

    [RelayCommand]
    private async Task PullWithModeAsync(GitPullMode mode)
    {
        PullMode = mode;
        await _errorHandler.RunGuardedAsync(async () =>
        {
            await _settingsStore.UpdateAsync(settings => settings with { PullMode = mode.ToString() });
        }, "save pull mode");
        await PullAsync();
    }

    /// <summary>Asks for one line of text (title, prompt, initial value); null when cancelled.</summary>
    public Func<string, string, string?, Task<string?>>? RequestInput { get; set; }

    // Like GitKraken, a push needs no confirmation; what needs a decision is a branch without
    // upstream (where to?) or a rejected push (force with lease?).
    [RelayCommand]
    private Task PushAsync() => PushNowAsync();

    /// <summary>Pushes the current branch, asking for an upstream or a force push when git needs one.</summary>
    public Task PushNowAsync() => RunRemoteAsync(async () =>
    {
        var path = _repositoryPath!;
        switch (await _pushService.PushAsync(path))
        {
            case PushOutcome.Pushed:
                _notifications.Show("Pushed to the remote");
                break;

            case PushOutcome.NeedsUpstream:
                var suggestion = await _pushService.SuggestUpstreamAsync(path);
                var upstream = RequestInput is null
                    ? suggestion
                    : await RequestInput("Push", "This branch has no upstream yet. Push it to (remote/branch) and track it:", suggestion);
                if (!string.IsNullOrWhiteSpace(upstream))
                {
                    await _pushService.PushWithUpstreamAsync(path, upstream.Trim());
                    _notifications.Show($"Pushed and now tracking {upstream.Trim()}");
                }

                break;

            case PushOutcome.Rejected:
                var force = RequestConfirmation is not null && await RequestConfirmation.Invoke(
                    "Push rejected",
                    "The remote branch has commits that are not in your branch (pull them first to keep them).\n\n" +
                    "Force push anyway? It uses --force-with-lease: it still refuses if the remote moved since your last fetch.");
                if (force)
                {
                    await _pushService.ForcePushAsync(path);
                    _notifications.Show("Force-pushed (with lease)");
                }

                break;
        }
    }, "push");

    private Task RunRemoteAsync(Func<Task> operation, string operationName) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation();
        }
        finally
        {
            IsBusy = false;
            // Even a failed pull can leave the branch or working directory changed.
            _changeNotifier.NotifyChanged();
        }
    }, operationName);
}
