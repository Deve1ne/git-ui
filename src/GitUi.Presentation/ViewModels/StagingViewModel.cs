using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.Entities;
using GitUi.Domain.ValueObjects;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public partial class StagingViewModel : ObservableObject
{
    private readonly IGitRepositoryReader _reader;
    private readonly StagingService _stagingService;
    private readonly UndoService _undoService;
    private readonly RefOperationsService _refOperations;
    private readonly ConflictResolutionService _conflicts;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private readonly IGitRepositoryWriter _writer;
    private readonly NotificationService _notifications;
    private string? _repositoryPath;
    private IReadOnlyList<DiffHunk> _hunks = Array.Empty<DiffHunk>();

    public MergeToolViewModel MergeTool { get; }

    /// <summary>Unresolved conflicts, listed apart from ordinary changes like GitKraken's "Conflicted Files".</summary>
    public ObservableCollection<FileChange> ConflictedChanges { get; } = new();

    public bool HasConflicts => ConflictedChanges.Count > 0;

    [ObservableProperty]
    private FileChange? selectedConflictedChange;

    public ObservableCollection<FileChange> UnstagedChanges { get; } = new();

    public ObservableCollection<FileChange> StagedChanges { get; } = new();

    /// <summary>The open file's diff rows (unified or split, per the shared diff options).</summary>
    public DiffPresenter Diff { get; }

    public ObservableCollection<DiffRow> DiffRows => Diff.Rows;

    /// <summary>Rows selected in the diff view; set by the view on every selection change.</summary>
    public IReadOnlyList<DiffRow> SelectedDiffRows { get; set; } = Array.Empty<DiffRow>();

    // The two lists keep their own selection: binding both ListBoxes to one SelectedItem would let
    // the list that does not contain the item push null back and cancel the other's selection.
    [ObservableProperty]
    private FileChange? selectedUnstagedChange;

    [ObservableProperty]
    private FileChange? selectedStagedChange;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiffVisible), nameof(CanStagePartially), nameof(CanDiscardPartially), nameof(PartialStageVerb))]
    private FileChange? selectedChange;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand), nameof(CommitAndPushCommand))]
    [NotifyPropertyChangedFor(nameof(SummaryCharactersLeft))]
    private string commitSummary = string.Empty;

    [ObservableProperty]
    private string commitDescription = string.Empty;

    /// <summary>GitKraken counts down from 72, the usual limit for a summary line.</summary>
    public int SummaryCharactersLeft => 72 - CommitSummary.Length;

    /// <summary>"Amend previous commit": the next commit replaces HEAD.</summary>
    [ObservableProperty]
    private bool isAmend;

    /// <summary>Commit with <c>--no-verify</c>, skipping pre-commit and commit-msg hooks.</summary>
    [ObservableProperty]
    private bool skipHooks;

    public bool IsDiffVisible => SelectedChange is not null;

    /// <summary>A merge, rebase, cherry-pick or revert stopped on conflicts, shown as a banner with Continue / Abort.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOperationInProgress), nameof(OperationBanner))]
    private GitOperationState operationState;

    public bool IsOperationInProgress => OperationState != GitOperationState.None;

    public string OperationBanner => OperationState switch
    {
        GitOperationState.Merge => "Merge in progress: resolve the conflicts, stage the files, then Continue.",
        GitOperationState.Rebase => "Rebase in progress: resolve the conflicts, stage the files, then Continue.",
        GitOperationState.CherryPick => "Cherry pick in progress: resolve the conflicts, stage the files, then Continue.",
        GitOperationState.Revert => "Revert in progress: resolve the conflicts, stage the files, then Continue.",
        _ => string.Empty,
    };

    /// <summary>
    /// Hunk/line staging needs both sides of the file in git; new, deleted and renamed files are
    /// staged as a whole.
    /// </summary>
    public bool CanStagePartially => SelectedChange?.ChangeType == FileChangeType.Modified && Diff.ShowsRealHunks;

    /// <summary>Hunks and lines can be discarded from the working tree, i.e. from an unstaged diff.</summary>
    public bool CanDiscardPartially => CanStagePartially && SelectedChange?.IsStaged == false;

    /// <summary>Raised to open a file's history (false) or blame (true) in the file view.</summary>
    public event Action<string, bool>? FileViewRequested;

    [RelayCommand]
    private Task OpenExternalDiffAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is { } path && SelectedChange is { } change)
        {
            await _writer.OpenDiffToolAsync(path, change.Path, change.IsStaged, commitSha: null);
        }
    }, "open external diff tool");

    [RelayCommand]
    private Task OpenExternalMergeToolAsync(FileChange change) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is { } path)
        {
            try
            {
                await _writer.OpenMergeToolAsync(path, change.Path);
            }
            finally
            {
                await ReloadAsync();
            }
        }
    }, "open external merge tool");

    /// <summary>Asks for one line of text (title, prompt, initial value); null when cancelled.</summary>
    public Func<string, string, string?, Task<string?>>? RequestInput { get; set; }

    public Func<string, Task>? RequestClipboard { get; set; }

    private string? FullPath(FileChange change) => _repositoryPath is null ? null : Path.GetFullPath(Path.Combine(_repositoryPath, change.Path));

    [RelayCommand]
    private Task OpenFileAsync(FileChange change) => _errorHandler.RunGuardedAsync(() =>
    {
        if (FullPath(change) is { } file)
        {
            ShellLauncher.OpenFile(file);
        }

        return Task.CompletedTask;
    }, "open file");

    [RelayCommand]
    private void RevealFile(FileChange change)
    {
        if (FullPath(change) is { } file)
        {
            ShellLauncher.RevealInFolder(file);
        }
    }

    [RelayCommand]
    private Task CopyFilePathAsync(FileChange change) =>
        RequestClipboard is not null && FullPath(change) is { } file ? RequestClipboard(file) : Task.CompletedTask;

    [RelayCommand]
    private Task AddToGitignoreAsync(FileChange change) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path || RequestInput is null)
        {
            return;
        }

        var file = change.Path.Replace('\\', '/');
        var extension = Path.GetExtension(file);
        var hint = string.IsNullOrEmpty(extension) ? "e.g. /build/ for a folder" : $"e.g. *{extension} for every {extension} file";
        var pattern = await RequestInput("Add to .gitignore", $"Pattern to ignore ({hint}):", "/" + file);
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        await _writer.AddToGitignoreAsync(path, pattern);
        await ReloadAsync();
        _changeNotifier.NotifyChanged();
    }, "add to .gitignore");

    [RelayCommand]
    private void ShowFileHistory(FileChange change) => FileViewRequested?.Invoke(change.Path, false);

    [RelayCommand]
    private void ShowFileBlame(FileChange change) => FileViewRequested?.Invoke(change.Path, true);

    /// <summary>Asks the user to confirm a destructive action (title, message); unset means "yes".</summary>
    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }

    public string PartialStageVerb => SelectedChange?.IsStaged == true ? "Unstage" : "Stage";

    public int TotalChangeCount => ConflictedChanges.Count + UnstagedChanges.Count + StagedChanges.Count;

    public string FileChangesSummary => TotalChangeCount switch
    {
        0 => "No file changes",
        1 => "1 file change",
        var count => $"{count} file changes",
    };

    public string CommitButtonLabel => IsAmend ? "Amend previous commit" : StagedChanges.Count switch
    {
        0 => "Stage files to commit",
        1 => "Commit changes to 1 file",
        var count => $"Commit changes to {count} files",
    };

    public StagingViewModel(
        IGitRepositoryReader reader,
        StagingService stagingService,
        UndoService undoService,
        RefOperationsService refOperations,
        ConflictResolutionService conflicts,
        MergeToolViewModel mergeTool,
        RepositoryChangeNotifier changeNotifier,
        CommandErrorHandler errorHandler,
        DiffSettings diffSettings,
        IGitRepositoryWriter writer,
        NotificationService notifications)
    {
        _notifications = notifications;
        _writer = writer;
        _diffSettings = diffSettings;
        diffSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffSettings.FilesAsTree))
            {
                RebuildFileRows();
            }
        };
        // Hunk / line staging works on git's real hunks: it is offered in the unified view
        // without the whitespace filter only.
        Diff = new DiffPresenter(diffSettings);
        Diff.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffPresenter.ShowsRealHunks))
            {
                OnPropertyChanged(nameof(CanStagePartially));
                OnPropertyChanged(nameof(CanDiscardPartially));
            }
        };
        _reader = reader;
        _stagingService = stagingService;
        _undoService = undoService;
        _refOperations = refOperations;
        _conflicts = conflicts;
        MergeTool = mergeTool;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
        MergeTool.Closed += () => _ = _errorHandler.RunGuardedAsync(ReloadAsync, "reload after conflict resolution");
    }

    public void Clear()
    {
        _repositoryPath = null;
        IsAmend = false;
        ConflictedChanges.Clear();
        OperationState = GitOperationState.None;
        UnstagedChanges.Clear();
        StagedChanges.Clear();
        CloseDiff();
        RaiseCountsChanged();
    }

    public async Task LoadAsync(string repositoryPath)
    {
        var reselect = string.Equals(repositoryPath, _repositoryPath, StringComparison.OrdinalIgnoreCase) ? SelectedChange : null;
        _repositoryPath = repositoryPath;
        var status = await _reader.GetStatusAsync(repositoryPath);
        OperationState = await _refOperations.GetOperationStateAsync(repositoryPath);

        UnstagedChanges.Clear();
        StagedChanges.Clear();
        ConflictedChanges.Clear();
        foreach (var change in status)
        {
            var list = change.ChangeType == FileChangeType.Conflicted ? ConflictedChanges
                : change.IsStaged ? StagedChanges
                : UnstagedChanges;
            list.Add(change);
        }

        RaiseCountsChanged();

        // Keep the open diff after a reload when the file is still on the same side of the index,
        // e.g. after staging some of its hunks; otherwise go back to the graph.
        var sameFile = reselect is null
            ? null
            : (reselect.IsStaged ? StagedChanges : UnstagedChanges).FirstOrDefault(c => c.Path == reselect.Path);

        if (sameFile is null)
        {
            CloseDiff();
        }
        else
        {
            // SelectedChange first: the list-selection handlers then see the file as already shown.
            SelectedChange = sameFile;
            if (sameFile.IsStaged)
            {
                SelectedStagedChange = sameFile;
            }
            else
            {
                SelectedUnstagedChange = sameFile;
            }

            await LoadDiffAsync(sameFile);
        }
    }

    private void RaiseCountsChanged()
    {
        RebuildFileRows();
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(TotalChangeCount));
        OnPropertyChanged(nameof(FileChangesSummary));
        OnPropertyChanged(nameof(CommitButtonLabel));
        CommitCommand.NotifyCanExecuteChanged();
        CommitAndPushCommand.NotifyCanExecuteChanged();
    }

    // Selecting a conflicted file opens the merge tool instead of a diff.
    partial void OnSelectedConflictedChangeChanged(FileChange? value)
    {
        if (value is null || _repositoryPath is null)
        {
            return;
        }

        CloseDiff();
        var path = _repositoryPath;
        _ = _errorHandler.RunGuardedAsync(() => MergeTool.OpenAsync(path, value.Path), "open merge tool");
        SelectedConflictedChange = null;
    }

    [RelayCommand]
    private Task TakeCurrentAsync(FileChange change) => ResolveAsync(() => _conflicts.TakeSideAsync(_repositoryPath!, change.Path, ConflictSide.Ours), "take current version");

    [RelayCommand]
    private Task TakeIncomingAsync(FileChange change) => ResolveAsync(() => _conflicts.TakeSideAsync(_repositoryPath!, change.Path, ConflictSide.Theirs), "take incoming version");

    [RelayCommand]
    private Task MarkResolvedAsync(FileChange change) => ResolveAsync(() => _conflicts.MarkResolvedAsync(_repositoryPath!, change.Path), "mark resolved");

    [RelayCommand]
    private Task MarkAllResolvedAsync() =>
        ResolveAsync(() => _conflicts.MarkAllResolvedAsync(_repositoryPath!, ConflictedChanges.Select(c => c.Path).ToList()), "mark all resolved");

    /// <summary>Runs the diff3 auto-resolution on every conflicted file; the ones with a true conflict stay listed.</summary>
    [RelayCommand]
    private Task AutoResolveAllAsync() => ResolveAsync(async () =>
    {
        var files = ConflictedChanges.Select(c => c.Path).ToList();
        var resolved = 0;
        foreach (var file in files)
        {
            if (await _conflicts.TryAutoResolveAsync(_repositoryPath!, file))
            {
                resolved++;
            }
        }

        _notifications.Show(resolved == 0
            ? "No conflict could be resolved automatically: they need a choice"
            : $"Resolved {resolved} of {files.Count} conflicted file{(files.Count == 1 ? string.Empty : "s")} automatically");
    }, "auto-resolve conflicts");

    [RelayCommand]
    private Task TakeAllIncomingAsync() => ResolveAsync(async () =>
    {
        foreach (var change in ConflictedChanges.ToList())
        {
            await _conflicts.TakeSideAsync(_repositoryPath!, change.Path, ConflictSide.Theirs);
        }
    }, "take incoming for all files");

    private Task ResolveAsync(Func<Task> action, string operationName) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        await action();
        await ReloadAsync();
    }, operationName);

    private readonly DiffSettings _diffSettings;
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);

    /// <summary>The unstaged / staged lists as shown: full paths, or a folder tree ("Tree").</summary>
    public ObservableCollection<ChangeFileRow> UnstagedRows { get; } = new();

    public ObservableCollection<ChangeFileRow> StagedRows { get; } = new();

    [ObservableProperty]
    private ChangeFileRow? selectedUnstagedRow;

    [ObservableProperty]
    private ChangeFileRow? selectedStagedRow;

    // A file row selects its change (opening the diff); a folder row folds or unfolds.
    partial void OnSelectedUnstagedRowChanged(ChangeFileRow? value) => OnRowPicked(value, staged: false);

    partial void OnSelectedStagedRowChanged(ChangeFileRow? value) => OnRowPicked(value, staged: true);

    private void OnRowPicked(ChangeFileRow? row, bool staged)
    {
        if (row is null)
        {
            return;
        }

        if (row.Item is { } change)
        {
            if (staged)
            {
                SelectedStagedChange = change;
            }
            else
            {
                SelectedUnstagedChange = change;
            }

            return;
        }

        ToggleFolder(row);
    }

    [RelayCommand]
    private void ToggleFolder(ChangeFileRow row)
    {
        if (row.FolderKey is { } key && !_collapsedFolders.Remove(key))
        {
            _collapsedFolders.Add(key);
        }

        RebuildFileRows();
    }

    private void RebuildFileRows()
    {
        Fill(UnstagedRows, UnstagedChanges, "unstaged");
        Fill(StagedRows, StagedChanges, "staged");
        SyncRowSelection();

        void Fill(ObservableCollection<ChangeFileRow> target, IEnumerable<FileChange> changes, string prefix)
        {
            target.Clear();
            var collapsed = _collapsedFolders.Where(k => k.StartsWith(prefix + ":", StringComparison.Ordinal))
                .Select(k => k[(prefix.Length + 1)..])
                .ToHashSet(StringComparer.Ordinal);
            foreach (var (label, depth, key, item, expanded) in FileRow<FileChange>.Layout(changes, c => c.Path, _diffSettings.FilesAsTree, collapsed))
            {
                target.Add(new ChangeFileRow(label, depth, key is null ? null : $"{prefix}:{key}", item, expanded));
            }
        }
    }

    private void SyncRowSelection()
    {
        SelectedUnstagedRow = SelectedUnstagedChange is { } u ? UnstagedRows.FirstOrDefault(r => Equals(r.Item, u)) : null;
        SelectedStagedRow = SelectedStagedChange is { } st ? StagedRows.FirstOrDefault(r => Equals(r.Item, st)) : null;
    }

    partial void OnSelectedUnstagedChangeChanged(FileChange? value)
    {
        if (!Equals(SelectedUnstagedRow?.Item, value))
        {
            SelectedUnstagedRow = value is null ? null : UnstagedRows.FirstOrDefault(r => Equals(r.Item, value));
        }

        if (value is not null && !Equals(value, SelectedChange))
        {
            SelectedStagedChange = null;
            SelectedChange = value;
            _ = _errorHandler.RunGuardedAsync(() => LoadDiffAsync(value), "load diff");
        }
    }

    partial void OnSelectedStagedChangeChanged(FileChange? value)
    {
        if (!Equals(SelectedStagedRow?.Item, value))
        {
            SelectedStagedRow = value is null ? null : StagedRows.FirstOrDefault(r => Equals(r.Item, value));
        }

        if (value is not null && !Equals(value, SelectedChange))
        {
            SelectedUnstagedChange = null;
            SelectedChange = value;
            _ = _errorHandler.RunGuardedAsync(() => LoadDiffAsync(value), "load diff");
        }
    }

    private async Task LoadDiffAsync(FileChange? change)
    {
        if (_repositoryPath is null || change is null)
        {
            _hunks = Array.Empty<DiffHunk>();
            Diff.Clear();
            return;
        }

        _hunks = await _reader.GetDiffAsync(_repositoryPath, change.Path, change.IsStaged);
        Diff.Show(_hunks);
    }

    [RelayCommand]
    private void CloseDiff()
    {
        SelectedUnstagedChange = null;
        SelectedStagedChange = null;
        SelectedChange = null;
        _hunks = Array.Empty<DiffHunk>();
        Diff.Clear();
    }

    [RelayCommand]
    private Task StageHunkAsync(DiffRow header) => ApplyPartialAsync(header.HunkIndex, selectedLineIndexes: null, "stage hunk");

    [RelayCommand]
    private Task StageSelectedLinesAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        // A selection can span hunks: apply one patch per hunk, last hunk first so earlier
        // hunks' line numbers are not shifted by the patches already applied.
        foreach (var group in SelectedDiffRows.Where(r => r.IsChange).GroupBy(r => r.HunkIndex).OrderByDescending(g => g.Key))
        {
            await ApplyPartialCoreAsync(group.Key, group.Select(r => r.LineIndex).ToHashSet());
        }

        await ReloadAsync();
    }, "stage selected lines");

    private Task ApplyPartialAsync(int hunkIndex, IReadOnlySet<int>? selectedLineIndexes, string operationName) =>
        _errorHandler.RunGuardedAsync(async () =>
        {
            await ApplyPartialCoreAsync(hunkIndex, selectedLineIndexes);
            await ReloadAsync();
        }, operationName);

    private async Task ApplyPartialCoreAsync(int hunkIndex, IReadOnlySet<int>? selectedLineIndexes)
    {
        if (_repositoryPath is null || SelectedChange is not { } change || !CanStagePartially || hunkIndex >= _hunks.Count)
        {
            return;
        }

        var patch = DiffPatchBuilder.BuildPatch(change.Path.Replace('\\', '/'), _hunks[hunkIndex], selectedLineIndexes, forUnstage: change.IsStaged);
        if (patch is not null)
        {
            await _stagingService.ApplyPatchToIndexAsync(_repositoryPath, patch, reverse: change.IsStaged);
        }
    }

    private Task ReloadAsync() => _repositoryPath is null ? Task.CompletedTask : LoadAsync(_repositoryPath);

    [RelayCommand]
    private Task ToggleStageAsync(FileChange change) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        if (change.IsStaged)
        {
            await _stagingService.UnstageAsync(_repositoryPath, change.Path);
        }
        else
        {
            await _stagingService.StageAsync(_repositoryPath, change.Path);
        }

        await ReloadAsync();
    }, "stage or unstage file");

    [RelayCommand]
    private Task StageAllAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        foreach (var change in UnstagedChanges.ToList())
        {
            await _stagingService.StageAsync(_repositoryPath, change.Path);
        }

        await ReloadAsync();
    }, "stage all changes");

    [RelayCommand]
    private Task UnstageAllAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        foreach (var change in StagedChanges.ToList())
        {
            await _stagingService.UnstageAsync(_repositoryPath, change.Path);
        }

        await ReloadAsync();
    }, "unstage all changes");

    private Task<bool> ConfirmAsync(string title, string message) =>
        RequestConfirmation is null ? Task.FromResult(true) : RequestConfirmation.Invoke(title, message);

    [RelayCommand]
    private Task DiscardFileAsync(FileChange change) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null || change.IsStaged)
        {
            return;
        }

        var what = change.ChangeType == FileChangeType.Added ? "delete the new file" : "permanently discard unstaged changes to";
        if (!await ConfirmAsync("Discard changes", $"This will {what} \"{change.Path}\". Are you sure you want to continue?"))
        {
            return;
        }

        await _stagingService.DiscardAsync(_repositoryPath, new[] { change });
        await ReloadAsync();
        _changeNotifier.NotifyChanged();
    }, "discard changes");

    [RelayCommand]
    private Task DiscardAllAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null || TotalChangeCount == 0)
        {
            return;
        }

        if (!await ConfirmAsync("Discard all changes", "Discard all staged, unstaged and untracked changes? You can bring them back with Undo."))
        {
            return;
        }

        await _undoService.DiscardAllAsync(_repositoryPath);
        _notifications.Show("Discarded all changes (Undo brings them back)");
        await ReloadAsync();
        _changeNotifier.NotifyChanged();
    }, "discard all changes");

    [RelayCommand]
    private Task DiscardHunkAsync(DiffRow header) => DiscardPartialAsync(
        new[] { (header.HunkIndex, (IReadOnlySet<int>?)null) }, "Discard hunk", "discard hunk");

    [RelayCommand]
    private Task DiscardSelectedLinesAsync() => DiscardPartialAsync(
        SelectedDiffRows.Where(r => r.IsChange)
            .GroupBy(r => r.HunkIndex)
            .Select(g => (g.Key, (IReadOnlySet<int>?)g.Select(r => r.LineIndex).ToHashSet()))
            .ToList(),
        "Discard selected lines", "discard selected lines");

    private Task DiscardPartialAsync(IReadOnlyList<(int HunkIndex, IReadOnlySet<int>? Lines)> selection, string title, string operationName) =>
        _errorHandler.RunGuardedAsync(async () =>
        {
            if (_repositoryPath is null || SelectedChange is not { } change || !CanDiscardPartially || selection.Count == 0)
            {
                return;
            }

            if (!await ConfirmAsync(title, $"This will permanently discard the selected changes to \"{change.Path}\". Are you sure you want to continue?"))
            {
                return;
            }

            // The working tree holds the new side, like the index when unstaging: build the patch
            // that way and apply it in reverse. Last hunk first so earlier line numbers hold.
            foreach (var (hunkIndex, lines) in selection.OrderByDescending(s => s.HunkIndex))
            {
                if (hunkIndex >= _hunks.Count)
                {
                    continue;
                }

                var patch = DiffPatchBuilder.BuildPatch(change.Path.Replace('\\', '/'), _hunks[hunkIndex], lines, forUnstage: true);
                if (patch is not null)
                {
                    await _stagingService.DiscardPatchAsync(_repositoryPath, patch);
                }
            }

            await ReloadAsync();
            _changeNotifier.NotifyChanged();
        }, operationName);

    [RelayCommand]
    private Task ContinueOperationAsync() => RunOperationAsync(_refOperations.ContinueOperationAsync, "continue");

    [RelayCommand]
    private Task AbortOperationAsync() => RunOperationAsync(_refOperations.AbortOperationAsync, "abort");

    private Task RunOperationAsync(Func<string, GitOperationState, CancellationToken, Task> operation, string verb) =>
        _errorHandler.RunGuardedAsync(async () =>
        {
            if (_repositoryPath is null || OperationState == GitOperationState.None)
            {
                return;
            }

            try
            {
                await operation(_repositoryPath, OperationState, CancellationToken.None);
            }
            finally
            {
                _changeNotifier.NotifyChanged();
            }
        }, $"{verb} {OperationState.ToString().ToLowerInvariant()}");

    // An amend may only reword the previous commit, so it does not need staged files.
    private bool CanCommit() => (StagedChanges.Count > 0 || IsAmend) && !string.IsNullOrWhiteSpace(CommitSummary);

    /// <summary>Raised after "Commit and push" committed successfully; the host runs the push.</summary>
    public event Func<Task>? PushAfterCommitRequested;

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private Task CommitAsync() => _errorHandler.RunGuardedAsync(() => CommitCoreAsync(), "commit");

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private Task CommitAndPushAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (await CommitCoreAsync() && PushAfterCommitRequested is { } push)
        {
            await push();
        }
    }, "commit and push");

    private async Task<bool> CommitCoreAsync()
    {
        if (_repositoryPath is null)
        {
            return false;
        }

        // Summary line, blank line, body — the shape git log and every host expect.
        var message = string.IsNullOrWhiteSpace(CommitDescription)
            ? CommitSummary.Trim()
            : $"{CommitSummary.Trim()}\n\n{CommitDescription.Trim()}";

        await _undoService.CommitAsync(_repositoryPath, message, new GitCommitOptions(IsAmend, SkipHooks));
        _notifications.Show($"{(IsAmend ? "Amended" : "Committed")}: {CommitSummary.Trim()}");
        _amendMessage = null;
        IsAmend = false;
        CommitSummary = string.Empty;
        CommitDescription = string.Empty;
        _changeNotifier.NotifyChanged();
        return true;
    }

    // The message the amend checkbox loaded, so unticking it only clears text the user did not change.
    private (string Summary, string Description)? _amendMessage;

    partial void OnIsAmendChanged(bool value)
    {
        OnPropertyChanged(nameof(CommitButtonLabel));
        CommitCommand.NotifyCanExecuteChanged();
        CommitAndPushCommand.NotifyCanExecuteChanged();

        if (value && string.IsNullOrWhiteSpace(CommitSummary) && string.IsNullOrWhiteSpace(CommitDescription) && _repositoryPath is { } path)
        {
            _ = _errorHandler.RunGuardedAsync(async () =>
            {
                var head = (await _reader.GetRepositoryAsync(path)).HeadSha;
                if (head is null || !IsAmend)
                {
                    return;
                }

                var details = await _reader.GetCommitDetailsAsync(path, head);
                CommitSummary = details.Summary;
                CommitDescription = details.Body;
                _amendMessage = (CommitSummary, CommitDescription);
            }, "load the previous commit message");
        }
        else if (!value && _amendMessage is { } loaded && loaded == (CommitSummary, CommitDescription))
        {
            CommitSummary = string.Empty;
            CommitDescription = string.Empty;
            _amendMessage = null;
        }
    }
}
