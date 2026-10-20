using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Hosting;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>One open pull request in the ref panel.</summary>
public sealed record PullRequestRow(PullRequest PullRequest)
{
    public string Header => $"#{PullRequest.Number} {PullRequest.Title}";

    public string Detail => $"{PullRequest.Author} · {PullRequest.SourceBranch} → {PullRequest.TargetBranch}{(PullRequest.IsFromFork ? " (fork)" : string.Empty)}";

    public string ToolTip => $"{PullRequest.Title}\n{Detail}\n{PullRequest.WebUrl}\nDouble-click to open it in the browser.";
}

/// <summary>
/// PULL REQUESTS section of the ref panel, like GitKraken's: the open pull requests (merge requests
/// on GitLab) of the repository behind the remote, open one in the browser, check out its branch,
/// or start a new one. Network errors stay in the section instead of raising dialogs, since the list
/// reloads in the background.
/// </summary>
public partial class PullRequestsViewModel : ObservableObject
{
    /// <summary>How long a loaded list stays fresh: repository reloads (after every stage, commit...) do not call the API each time.</summary>
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(2);

    private readonly HostingService _hosting;
    private readonly IGitRepositoryReader _reader;
    private readonly IGitRepositoryWriter _writer;
    private readonly UndoService _undoService;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private readonly NotificationService _notifications;
    private string? _repositoryPath;
    private RepositoryHosting? _repositoryHosting;
    private DateTime _loadedAt;
    private int _loadVersion;

    public PullRequestsViewModel(
        HostingService hosting,
        IGitRepositoryReader reader,
        IGitRepositoryWriter writer,
        UndoService undoService,
        RepositoryChangeNotifier changeNotifier,
        CommandErrorHandler errorHandler,
        NotificationService notifications)
    {
        _hosting = hosting;
        _reader = reader;
        _writer = writer;
        _undoService = undoService;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
        _notifications = notifications;
        _hosting.AccountsChanged += () => Dispatcher.UIThread.Post(() => _ = ReloadAsync());
    }

    public ObservableCollection<PullRequestRow> PullRequests { get; } = new();

    /// <summary>The remote is on a supported service: the section shows.</summary>
    [ObservableProperty]
    private bool isAvailable;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool needsAccount;

    /// <summary>Why the list is empty or stale (no account, an API error), shown in the section.</summary>
    [ObservableProperty]
    private string? message;

    [ObservableProperty]
    private string header = "PULL REQUESTS";

    /// <summary>Opens the Accounts window and waits until it closes.</summary>
    public Func<Task>? RequestAccounts { get; set; }

    /// <summary>Shows the create dialog; the created pull request, or null when cancelled.</summary>
    public Func<CreatePullRequestViewModel, Task<PullRequest?>>? RequestCreateDialog { get; set; }

    public Func<string, Task>? RequestClipboard { get; set; }

    public void Clear()
    {
        _loadVersion++;
        _repositoryPath = null;
        _repositoryHosting = null;
        PullRequests.Clear();
        IsAvailable = false;
        NeedsAccount = false;
        Message = null;
        Header = "PULL REQUESTS";
    }

    /// <summary>Loads the list for the repository, unless it was loaded for it less than <see cref="Freshness"/> ago.</summary>
    public async Task LoadAsync(string repositoryPath, bool force = false)
    {
        var sameRepository = string.Equals(_repositoryPath, repositoryPath, StringComparison.OrdinalIgnoreCase);
        if (sameRepository && !force && DateTime.UtcNow - _loadedAt < Freshness)
        {
            return;
        }

        if (!sameRepository)
        {
            PullRequests.Clear();
        }

        _repositoryPath = repositoryPath;
        _loadedAt = DateTime.UtcNow;
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var hosting = await _hosting.FindHostingAsync(repositoryPath);
            if (version != _loadVersion)
            {
                return;
            }

            _repositoryHosting = hosting;
            IsAvailable = hosting is not null;
            if (hosting is null)
            {
                PullRequests.Clear();
                return;
            }

            var name = hosting.Repository.Provider.PullRequestName();
            Header = $"{name.ToUpperInvariant()}S";
            NeedsAccount = hosting.Account is null;
            if (hosting.Account is null)
            {
                PullRequests.Clear();
                Message = $"Connect a {hosting.Repository.Provider.DisplayName()} account to see the {name}s of {hosting.Repository.FullPath}.";
                return;
            }

            var pulls = await _hosting.GetPullRequestsAsync(hosting, interactive: force);
            if (version != _loadVersion)
            {
                return;
            }

            PullRequests.Clear();
            foreach (var pull in pulls.OrderByDescending(p => p.Number))
            {
                PullRequests.Add(new PullRequestRow(pull));
            }

            Header = $"{name.ToUpperInvariant()}S ({pulls.Count})";
            Message = pulls.Count == 0 ? $"No open {name}." : null;
        }
        catch (HostingException ex)
        {
            if (version == _loadVersion)
            {
                Message = ex.Message;
                // Try again on the next reload rather than waiting for the freshness delay.
                _loadedAt = DateTime.MinValue;
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private Task ReloadAsync() => _repositoryPath is { } path ? LoadAsync(path, force: true) : Task.CompletedTask;

    [RelayCommand]
    private Task RefreshAsync() => ReloadAsync();

    [RelayCommand]
    private async Task ConnectAccountAsync()
    {
        if (RequestAccounts is not null)
        {
            await RequestAccounts();
        }
    }

    [RelayCommand]
    private Task OpenInBrowserAsync(PullRequestRow row) =>
        _errorHandler.RunGuardedAsync(() =>
        {
            ShellLauncher.OpenUrl(row.PullRequest.WebUrl);
            return Task.CompletedTask;
        }, "open pull request");

    [RelayCommand]
    private Task CopyLinkAsync(PullRequestRow row) => RequestClipboard?.Invoke(row.PullRequest.WebUrl) ?? Task.CompletedTask;

    /// <summary>Fetches the remote, then checks out the pull request's branch (its local twin when there is one).</summary>
    [RelayCommand]
    private Task CheckoutAsync(PullRequestRow row) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path || _repositoryHosting is not { } hosting)
        {
            return;
        }

        var pull = row.PullRequest;
        if (pull.IsFromFork)
        {
            throw new InvalidOperationException($"The branch of #{pull.Number} lives in a fork ({pull.SourceRepository}): add that fork as a remote to check it out.");
        }

        try
        {
            await _writer.FetchRemoteAsync(path, hosting.RemoteName);
            var branches = await _reader.GetBranchesAsync(path);
            if (branches.Any(b => b.IsLocal && b.Name == pull.SourceBranch))
            {
                await _undoService.CheckoutAsync(path, pull.SourceBranch);
            }
            else
            {
                await _writer.CheckoutRemoteBranchAsync(path, $"{hosting.RemoteName}/{pull.SourceBranch}");
            }

            _notifications.Show($"Checked out {pull.SourceBranch} (#{pull.Number})");
        }
        finally
        {
            _changeNotifier.NotifyChanged();
        }
    }, "check out pull request");

    /// <summary>Create from the checked-out branch (the section's + button).</summary>
    [RelayCommand]
    private async Task CreateAsync()
    {
        if (_repositoryPath is { } path)
        {
            var head = (await _reader.GetBranchesAsync(path)).FirstOrDefault(b => b.IsHead && b.IsLocal);
            await StartCreateAsync(head?.Name, null);
        }
    }

    /// <summary>
    /// Opens the create dialog for a local branch (pushed first when needed) or a branch already on the
    /// remote (<paramref name="remoteBranch"/>, e.g. "origin/feature").
    /// </summary>
    public Task StartCreateAsync(string? localBranch, string? remoteBranch) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path)
        {
            return;
        }

        var hosting = await _hosting.FindHostingAsync(path)
            ?? throw new InvalidOperationException("None of this repository's remotes is on GitHub, GitLab, Azure DevOps or Bitbucket.");
        if (hosting.Account is null)
        {
            if (RequestAccounts is not null)
            {
                await RequestAccounts();
            }

            hosting = await _hosting.FindHostingAsync(path);
            if (hosting?.Account is null)
            {
                return;
            }
        }

        if (localBranch is null && remoteBranch is null)
        {
            throw new InvalidOperationException("Check out a branch first: HEAD is detached.");
        }

        var dialog = new CreatePullRequestViewModel(_hosting, _reader, _writer, path, hosting, localBranch, remoteBranch);
        await dialog.InitializeAsync();
        if (RequestCreateDialog is null || await RequestCreateDialog(dialog) is not { } created)
        {
            return;
        }

        _notifications.Show($"{hosting.Repository.Provider.DisplayName()} {hosting.Repository.Provider.PullRequestName()} #{created.Number} created");
        if (dialog.OpenAfterCreating)
        {
            ShellLauncher.OpenUrl(created.WebUrl);
        }

        _changeNotifier.NotifyChanged();
        await LoadAsync(path, force: true);
    }, "create pull request");
}
