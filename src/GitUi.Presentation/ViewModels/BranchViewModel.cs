using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public partial class BranchViewModel : ObservableObject
{
    private readonly IGitRepositoryReader _reader;
    private readonly UndoService _undoService;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;

    /// <summary>Every branch, unfiltered; the section lists below follow <see cref="FilterText"/>.</summary>
    public ObservableCollection<GitBranch> Branches { get; } = new();
    public ObservableCollection<GitBranch> LocalBranches { get; } = new();
    public ObservableCollection<GitBranch> RemoteBranches { get; } = new();
    public ObservableCollection<GitTag> Tags { get; } = new();

    [ObservableProperty]
    private string newBranchName = string.Empty;

    [ObservableProperty]
    private string? currentBranchName;

    private IReadOnlyList<GitTag> _allTags = Array.Empty<GitTag>();

    /// <summary>Case-insensitive filter on branch and tag names (matches anywhere, e.g. "39749" finds "fix/39749").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltering))]
    private string filterText = string.Empty;

    public bool IsFiltering => !string.IsNullOrWhiteSpace(FilterText);

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    private bool Matches(string name) => !IsFiltering || name.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase);

    private void ApplyFilter()
    {
        LocalBranches.Clear();
        RemoteBranches.Clear();
        // origin/HEAD is a pointer to the remote's default branch, not a branch of its own.
        foreach (var branch in Branches.Where(b => Matches(b.Name) && !(b.IsRemote && b.Name.EndsWith("/HEAD", StringComparison.Ordinal))))
        {
            (branch.IsRemote ? RemoteBranches : LocalBranches).Add(branch);
        }

        Tags.Clear();
        foreach (var tag in _allTags.Where(t => Matches(t.Name)))
        {
            Tags.Add(tag);
        }

        RebuildTree();
    }

    /// <summary>LOCAL as a folder tree ("feature/x" under "feature").</summary>
    public ObservableCollection<RefRowViewModel> LocalRows { get; } = new();

    /// <summary>REMOTE as a tree grouped by remote ("origin" &gt; "feature" &gt; "x").</summary>
    public ObservableCollection<RefRowViewModel> RemoteRows { get; } = new();

    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);
    private GraphRefFilter _graphFilter = GraphRefFilter.Empty;

    [ObservableProperty]
    private string graphFilterSummary = string.Empty;

    public bool IsGraphFiltered => !_graphFilter.IsEmpty;

    private void RebuildTree()
    {
        Fill(LocalRows, RefTree.Build(LocalBranches, "local", _collapsedFolders, expandAll: IsFiltering));
        // Every remote shows, even before its first fetch; a filter only keeps remotes with matches.
        Fill(RemoteRows, RefTree.Build(RemoteBranches, "remote", _collapsedFolders, expandAll: IsFiltering,
            rootFolders: IsFiltering ? null : Remotes.Select(r => r.Name)));

        GraphFilterSummary = _graphFilter.Solo.Count > 0
            ? $"Graph shows {_graphFilter.Solo.Count} soloed branch{(_graphFilter.Solo.Count == 1 ? string.Empty : "es")}"
            : _graphFilter.Hidden.Count > 0
                ? $"{_graphFilter.Hidden.Count} branch{(_graphFilter.Hidden.Count == 1 ? string.Empty : "es")} hidden from the graph"
                : string.Empty;
        OnPropertyChanged(nameof(IsGraphFiltered));

        void Fill(ObservableCollection<RefRowViewModel> target, IReadOnlyList<RefTreeRow> rows)
        {
            target.Clear();
            foreach (var row in rows)
            {
                var refName = row.Branch is null ? null : GraphRefFilter.RefName(row.Branch);
                target.Add(new RefRowViewModel(row, refName is not null && _graphFilter.Hidden.Contains(refName), refName is not null && _graphFilter.Solo.Contains(refName)));
            }
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    public bool HasSubmodules => Submodules.Count > 0;

    /// <summary>WORKTREES only shows once there is more than the main working tree, like GitKraken.</summary>
    public bool HasLinkedWorktrees => Worktrees.Count > 1;

    public ObservableCollection<GitRemote> Remotes { get; } = new();

    public ObservableCollection<GitSubmodule> Submodules { get; } = new();

    public ObservableCollection<GitWorktree> Worktrees { get; } = new();

    /// <summary>Asks for one line of text (title, prompt, initial value); null when cancelled.</summary>
    public Func<string, string, string?, Task<string?>>? RequestInput { get; set; }

    /// <summary>Opens a repository (a submodule or a worktree) in a new tab.</summary>
    public Action<string>? RequestOpenRepository { get; set; }

    private Task RunAsync(Func<string, Task> action, string operationName) => _errorHandler.RunGuardedAsync(async () =>
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
        }
    }, operationName);

    private async Task<string?> AskAsync(string title, string prompt, string? initial = null)
    {
        var answer = RequestInput is null ? null : await RequestInput(title, prompt, initial);
        return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
    }

    [RelayCommand]
    private Task AddRemoteAsync() => RunAsync(async path =>
    {
        if (await AskAsync("Add remote", "Remote name:", Remotes.Count == 0 ? "origin" : null) is not { } name
            || await AskAsync("Add remote", $"URL of '{name}':") is not { } url)
        {
            return;
        }

        await _writer.AddRemoteAsync(path, name, url);
        await _writer.FetchRemoteAsync(path, name);
    }, "add remote");

    // With the saved fetch options (prune, tags, submodules), like the toolbar Fetch.
    public Task FetchRemoteAsync(string remote) => RunAsync(async path =>
    {
        var moved = await _remotes.FetchWithOptionsAsync(path, remote);
        if (moved.Count > 0 && RequestConfirmation is not null
            && await RequestConfirmation("Tags moved on the server",
                $"These tags point to another commit on {remote} than here: {string.Join(", ", moved.Take(10))}{(moved.Count > 10 ? ", …" : string.Empty)}.\n\n" +
                "Everything else was fetched. Replace your local tags with the server's version (git fetch --force)?"))
        {
            await _remotes.UpdateMovedTagsAsync(path, remote);
        }
    }, $"fetch {remote}");

    public Task EditRemoteUrlAsync(string remote) => RunAsync(async path =>
    {
        var current = Remotes.FirstOrDefault(r => r.Name == remote)?.FetchUrl;
        if (await AskAsync("Edit remote", $"URL of '{remote}':", current) is { } url && url != current)
        {
            await _writer.SetRemoteUrlAsync(path, remote, url);
        }
    }, "edit remote URL");

    public Task RenameRemoteAsync(string remote) => RunAsync(async path =>
    {
        if (await AskAsync("Rename remote", $"New name for '{remote}':", remote) is { } name && name != remote)
        {
            await _writer.RenameRemoteAsync(path, remote, name);
        }
    }, "rename remote");

    public Task RemoveRemoteAsync(string remote) => RunAsync(async path =>
    {
        if (RequestConfirmation is null
            || await RequestConfirmation("Remove remote", $"Remove remote '{remote}' and its remote-tracking branches? Nothing is deleted on the server."))
        {
            await _writer.RemoveRemoteAsync(path, remote);
        }
    }, "remove remote");

    [RelayCommand]
    private Task UpdateSubmodulesAsync() => RunAsync(path => _writer.UpdateSubmodulesAsync(path, null), "update submodules");

    [RelayCommand]
    private Task UpdateSubmoduleAsync(GitSubmodule submodule) => RunAsync(path => _writer.UpdateSubmodulesAsync(path, submodule.Path), "update submodule");

    [RelayCommand]
    private void OpenSubmodule(GitSubmodule submodule)
    {
        if (_repositoryPath is { } path)
        {
            RequestOpenRepository?.Invoke(Path.GetFullPath(Path.Combine(path, submodule.Path)));
        }
    }

    [RelayCommand]
    private void OpenWorktree(GitWorktree worktree) => RequestOpenRepository?.Invoke(worktree.Path);

    [RelayCommand]
    private Task AddWorktreeAsync() => RunAsync(async path =>
    {
        if (await AskAsync("Add worktree", "New branch to check out in the worktree:") is not { } branch)
        {
            return;
        }

        // Default folder: next to the repository, named after it and the branch.
        var root = path.TrimEnd('\\', '/');
        var suggestion = Path.Combine(Path.GetDirectoryName(root) ?? root, $"{Path.GetFileName(root)}-{branch.Replace('/', '-')}");
        if (await AskAsync("Add worktree", "Folder for the new working tree:", suggestion) is not { } folder)
        {
            return;
        }

        await _writer.AddWorktreeAsync(path, folder, "HEAD", branch);
    }, "add worktree");

    [RelayCommand]
    private Task RemoveWorktreeAsync(GitWorktree worktree) => RunAsync(async path =>
    {
        if (worktree.IsMain)
        {
            throw new InvalidOperationException("The main working tree cannot be removed.");
        }

        if (RequestConfirmation is null
            || await RequestConfirmation("Remove worktree", $"Remove the working tree at {worktree.Path}? Its branch is kept; uncommitted changes there must be committed first."))
        {
            await _writer.RemoveWorktreeAsync(path, worktree.Path, force: false);
        }
    }, "remove worktree");

    [RelayCommand]
    private void ToggleFolder(RefRowViewModel row)
    {
        if (row.Row.FolderKey is not { } key)
        {
            return;
        }

        if (!_collapsedFolders.Remove(key))
        {
            _collapsedFolders.Add(key);
        }

        RebuildTree();
    }

    [RelayCommand]
    private Task ToggleHiddenAsync(RefRowViewModel row) => ChangeGraphFilterAsync(row, _graphFilters.ToggleHiddenAsync, "hide branch");

    [RelayCommand]
    private Task ToggleSoloAsync(RefRowViewModel row) => ChangeGraphFilterAsync(row, _graphFilters.ToggleSoloAsync, "solo branch");

    [RelayCommand]
    private Task ShowAllBranchesAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        await _graphFilters.ClearAsync(_repositoryPath);
        _changeNotifier.NotifyChanged();
    }, "show all branches");

    private Task ChangeGraphFilterAsync(RefRowViewModel row, Func<string, string, CancellationToken, Task> change, string operationName) =>
        _errorHandler.RunGuardedAsync(async () =>
        {
            if (_repositoryPath is null || row.Branch is not { } branch)
            {
                return;
            }

            await change(_repositoryPath, GraphRefFilter.RefName(branch), CancellationToken.None);
            _changeNotifier.NotifyChanged();
        }, operationName);

    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }

    private readonly GraphFilterService _graphFilters;
    private readonly IGitRepositoryWriter _writer;

    private readonly RemoteService _remotes;

    public BranchViewModel(IGitRepositoryReader reader, IGitRepositoryWriter writer, UndoService undoService, GraphFilterService graphFilters, RepositoryChangeNotifier changeNotifier, CommandErrorHandler errorHandler, RemoteService remotes)
    {
        _remotes = remotes;
        _reader = reader;
        _writer = writer;
        _undoService = undoService;
        _graphFilters = graphFilters;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
    }

    public void Clear()
    {
        _repositoryPath = null;
        Branches.Clear();
        LocalBranches.Clear();
        RemoteBranches.Clear();
        Tags.Clear();
        _allTags = Array.Empty<GitTag>();
        _graphFilter = GraphRefFilter.Empty;
        LocalRows.Clear();
        RemoteRows.Clear();
        Remotes.Clear();
        Submodules.Clear();
        Worktrees.Clear();
        CurrentBranchName = null;
    }

    public async Task LoadAsync(string repositoryPath)
    {
        if (!string.Equals(_repositoryPath, repositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            _collapsedFolders.Clear();
        }

        _repositoryPath = repositoryPath;
        var branches = await _reader.GetBranchesAsync(repositoryPath);
        var tags = await _reader.GetTagsAsync(repositoryPath);
        _graphFilter = await _graphFilters.GetAsync(repositoryPath);
        Replace(Remotes, await _reader.GetRemotesAsync(repositoryPath));
        Replace(Submodules, await _reader.GetSubmodulesAsync(repositoryPath));
        Replace(Worktrees, await _reader.GetWorktreesAsync(repositoryPath));
        OnPropertyChanged(nameof(HasSubmodules));
        OnPropertyChanged(nameof(HasLinkedWorktrees));

        Branches.Clear();
        foreach (var branch in branches)
        {
            Branches.Add(branch);
        }

        _allTags = tags;
        ApplyFilter();

        CurrentBranchName = branches.FirstOrDefault(b => b.IsHead)?.Name;
    }

    [RelayCommand]
    private Task CreateBranchAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null || string.IsNullOrWhiteSpace(NewBranchName))
        {
            return;
        }

        await _undoService.CreateBranchAsync(_repositoryPath, NewBranchName.Trim());
        NewBranchName = string.Empty;
        _changeNotifier.NotifyChanged();
    }, "create branch");

    [RelayCommand]
    private Task CheckoutAsync(GitBranch branch) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        await _undoService.CheckoutAsync(_repositoryPath, branch.Name);
        _changeNotifier.NotifyChanged();
    }, "checkout branch");

    [RelayCommand]
    private Task DeleteAsync(GitBranch branch) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        var confirmed = RequestConfirmation is null
            || await RequestConfirmation.Invoke("Delete branch", $"Delete local branch '{branch.Name}'? You can restore it with Undo.");

        if (!confirmed)
        {
            return;
        }

        await _undoService.DeleteBranchAsync(_repositoryPath, branch.Name, force: false);
        _changeNotifier.NotifyChanged();
    }, "delete branch");
}
