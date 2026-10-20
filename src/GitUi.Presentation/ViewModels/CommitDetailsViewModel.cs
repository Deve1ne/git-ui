using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Domain.Entities;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// Right-hand commit panel shown when a commit is selected in the graph (GitKraken's commit detail):
/// message, author / committer, SHA and parents, changed files with their line stats, and the diff
/// of the selected file in the centre.
/// </summary>
public partial class CommitDetailsViewModel : ObservableObject
{
    private readonly IGitRepositoryReader _reader;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;
    private int _loadGeneration;

    private readonly IGitRepositoryWriter _writer;

    private readonly GitUi.Application.Services.SigningService _signing;

    /// <summary>The commit's signature, when it has one ("Good signature by …").</summary>
    [ObservableProperty]
    private string? signatureText;

    [ObservableProperty]
    private bool isSignatureVerified;

    public CommitDetailsViewModel(IGitRepositoryReader reader, IGitRepositoryWriter writer, CommandErrorHandler errorHandler, DiffSettings diffSettings,
        GitUi.Application.Services.SigningService signing)
    {
        _reader = reader;
        _writer = writer;
        _signing = signing;
        _errorHandler = errorHandler;
        Diff = new DiffPresenter(diffSettings);
        _diffSettings = diffSettings;
        diffSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffSettings.FilesAsTree))
            {
                RebuildFileRows();
            }
        };
    }

    /// <summary>The selected file's diff rows (unified or split, per the shared diff options).</summary>
    public DiffPresenter Diff { get; }

    public Func<string, Task>? RequestClipboard { get; set; }

    /// <summary>Raised when the user asks to see a parent commit (the graph selects it).</summary>
    public event Action<string>? ParentRequested;

    public ObservableCollection<CommitFileChange> Files { get; } = new();

    private readonly DiffSettings _diffSettings;
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);

    /// <summary>What the file list shows: <see cref="Files"/> as paths or as a folder tree.</summary>
    public ObservableCollection<CommitFileRow> FileRows { get; } = new();

    [ObservableProperty]
    private CommitFileRow? selectedFileRow;

    // Picking a file row opens its diff; picking a folder row folds or unfolds it.
    partial void OnSelectedFileRowChanged(CommitFileRow? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Item is { } file)
        {
            SelectedFile = file;
            return;
        }

        ToggleFolder(value);
    }

    [RelayCommand]
    private void ToggleFolder(CommitFileRow row)
    {
        if (row.FolderKey is not { } key)
        {
            return;
        }

        if (!_collapsedFolders.Remove(key))
        {
            _collapsedFolders.Add(key);
        }

        RebuildFileRows();
    }

    private void RebuildFileRows()
    {
        var selected = SelectedFile;
        FileRows.Clear();
        foreach (var (label, depth, key, item, expanded) in FileRow<CommitFileChange>.Layout(Files, f => f.Path, _diffSettings.FilesAsTree, _collapsedFolders))
        {
            FileRows.Add(new CommitFileRow(label, depth, key, item, expanded));
        }

        SelectedFileRow = selected is null ? null : FileRows.FirstOrDefault(r => ReferenceEquals(r.Item, selected));
    }

    public ObservableCollection<DiffRow> DiffRows => Diff.Rows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(ShortSha), nameof(AuthorLine), nameof(CommitterLine), nameof(ShowCommitter), nameof(HasBody), nameof(FilesSummary), nameof(ParentShortShas))]
    private CommitDetails? details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiffVisible))]
    private CommitFileChange? selectedFile;

    public bool IsVisible => Details is not null;

    public bool IsDiffVisible => SelectedFile is not null;

    public string ShortSha => Details?.Sha[..Math.Min(7, Details.Sha.Length)] ?? string.Empty;

    public string AuthorLine => Details is { } d ? $"{d.AuthorName} <{d.AuthorEmail}>  ·  authored {d.AuthorDate.ToLocalTime():yyyy-MM-dd HH:mm}" : string.Empty;

    public string CommitterLine => Details is { } d ? $"{d.CommitterName} <{d.CommitterEmail}>  ·  committed {d.CommitDate.ToLocalTime():yyyy-MM-dd HH:mm}" : string.Empty;

    /// <summary>The committer is only worth a line when it differs from the author (rebases, cherry-picks, merges by a bot...).</summary>
    public bool ShowCommitter => Details is { } d && (d.CommitterName != d.AuthorName || d.CommitterEmail != d.AuthorEmail || d.CommitDate != d.AuthorDate);

    public bool HasBody => !string.IsNullOrWhiteSpace(Details?.Body);

    public IReadOnlyList<string> ParentShortShas => Details?.ParentShas.Select(p => p[..Math.Min(7, p.Length)]).ToList() ?? new List<string>();

    public string FilesSummary => Details is { } d
        ? $"{d.Files.Count} file{(d.Files.Count == 1 ? string.Empty : "s")} changed   +{d.TotalAdditions}  −{d.TotalDeletions}"
        : string.Empty;

    /// <summary>Called on every reload: only another repository (or none) closes the detail.</summary>
    public void SetRepositoryPath(string? repositoryPath)
    {
        if (string.Equals(_repositoryPath, repositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _repositoryPath = repositoryPath;
        Close();
    }

    /// <summary>"COMMIT" or "STASH", shown before the short SHA in the panel and diff headers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private string headerLabel = "COMMIT";

    public string HeaderText => $"{HeaderLabel} {ShortSha}";

    partial void OnDetailsChanged(CommitDetails? value) => OnPropertyChanged(nameof(HeaderText));

    public Task LoadStashAsync(GitStash stash) => LoadAsync(stash.Sha, "STASH");

    // Set while the panel compares two commits: file diffs then run between them.
    private string? _compareFromSha;

    /// <summary>GitKraken's Ctrl+click on a second commit: what changed from the older one to the newer one.</summary>
    public Task LoadComparisonAsync(string fromSha, string toSha) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path)
        {
            return;
        }

        var generation = ++_loadGeneration;
        var newer = await _reader.GetCommitDetailsAsync(path, toSha);
        var files = await _reader.GetComparisonAsync(path, fromSha, toSha);
        if (generation != _loadGeneration)
        {
            return;
        }

        SelectedFile = null;
        Diff.Clear();
        Files.Clear();
        foreach (var file in files)
        {
            Files.Add(file);
        }

        RebuildFileRows();

        _compareFromSha = fromSha;
        HeaderLabel = "COMPARE";
        Details = newer with
        {
            Summary = $"Comparing {fromSha[..7]} → {toSha[..7]}",
            Body = "Changes from the older commit to the newer one. Click a commit without Ctrl to leave the comparison.",
            ParentShas = new[] { fromSha },
            Files = files,
        };
    }, "compare commits");

    public Task LoadAsync(string sha) => LoadAsync(sha, "COMMIT");

    private Task LoadAsync(string sha, string headerLabel) => _errorHandler.RunGuardedAsync(async () =>
    {
        // A commit never changes: re-selecting the one already shown (graph reload) keeps the open file diff.
        if (Details?.Sha == sha && HeaderLabel == headerLabel)
        {
            return;
        }

        _compareFromSha = null;

        if (_repositoryPath is not { } path)
        {
            return;
        }

        // A newer selection wins over a slower, older load.
        var generation = ++_loadGeneration;
        var loaded = await _reader.GetCommitDetailsAsync(path, sha);
        if (generation != _loadGeneration)
        {
            return;
        }

        SelectedFile = null;
        Diff.Clear();
        Files.Clear();
        foreach (var file in loaded.Files)
        {
            Files.Add(file);
        }

        RebuildFileRows();

        HeaderLabel = headerLabel;
        // A stash's other parents are git internals (its index and untracked-files commits).
        Details = headerLabel == "STASH" ? loaded with { ParentShas = loaded.ParentShas.Take(1).ToList() } : loaded;

        // Checking a signature runs gpg / ssh-keygen: done after the details show.
        SignatureText = null;
        var signature = await _signing.GetSignatureAsync(path, sha);
        if (generation == _loadGeneration && signature.IsSigned)
        {
            IsSignatureVerified = signature.IsVerified;
            SignatureText = signature.Signer is { } signer ? $"{signature.Description} — {signer}" : signature.Description;
        }
    }, "load commit details");

    partial void OnSelectedFileChanged(CommitFileChange? value)
    {
        Diff.Clear();
        if (value is null)
        {
            SelectedFileRow = null;
        }

        if (value is null || Details is not { } commit || _repositoryPath is not { } path)
        {
            return;
        }

        _ = _errorHandler.RunGuardedAsync(async () =>
        {
            var hunks = _compareFromSha is { } from
                ? await _reader.GetComparisonFileDiffAsync(path, from, commit.Sha, value.Path)
                : await _reader.GetCommitFileDiffAsync(path, commit.Sha, value.Path);
            if (!ReferenceEquals(value, SelectedFile))
            {
                return;
            }

            Diff.Show(hunks);
        }, "load commit file diff");
    }

    [RelayCommand]
    public void Close()
    {
        _loadGeneration++;
        _compareFromSha = null;
        SelectedFile = null;
        Diff.Clear();
        Files.Clear();
        FileRows.Clear();
        Details = null;
    }

    [RelayCommand]
    private void CloseDiff() => SelectedFile = null;

    [RelayCommand]
    private Task OpenExternalDiffAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is { } path && Details is { } commit && SelectedFile is { } file)
        {
            await _writer.OpenDiffToolAsync(path, file.Path, staged: false, commitSha: commit.Sha, compareFromSha: _compareFromSha);
        }
    }, "open external diff tool");

    /// <summary>Raised to open a file's history (false) or blame (true) in the file view.</summary>
    public event Action<string, bool>? FileViewRequested;

    /// <summary>Asks the user to confirm (title, message); unset means "yes".</summary>
    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }

    /// <summary>Raised after the working directory changed (a file restored from a commit).</summary>
    public event Action? WorkingDirectoryChanged;

    [RelayCommand]
    private Task RestoreFileAsync(CommitFileChange file) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } path || Details is not { } commit)
        {
            return;
        }

        if (RequestConfirmation is not null && !await RequestConfirmation(
                "Checkout file",
                $"Replace \"{file.Path}\" in the working directory with its version from {ShortSha}? Uncommitted changes to it are lost."))
        {
            return;
        }

        await _writer.RestoreFileFromCommitAsync(path, commit.Sha, file.Path);
        WorkingDirectoryChanged?.Invoke();
    }, "checkout file from commit");

    [RelayCommand]
    private void RevealFile(CommitFileChange file)
    {
        if (_repositoryPath is { } path)
        {
            ShellLauncher.RevealInFolder(Path.GetFullPath(Path.Combine(path, file.Path)));
        }
    }

    [RelayCommand]
    private Task CopyFilePathAsync(CommitFileChange file) =>
        RequestClipboard is not null && _repositoryPath is { } path ? RequestClipboard(Path.GetFullPath(Path.Combine(path, file.Path))) : Task.CompletedTask;

    [RelayCommand]
    private void ShowFileHistory(CommitFileChange file) => FileViewRequested?.Invoke(file.Path, false);

    [RelayCommand]
    private void ShowFileBlame(CommitFileChange file) => FileViewRequested?.Invoke(file.Path, true);

    [RelayCommand]
    private Task CopySha() => Details is { } d && RequestClipboard is not null ? RequestClipboard(d.Sha) : Task.CompletedTask;

    [RelayCommand]
    private void ShowParent(string shortSha)
    {
        if (Details?.ParentShas.FirstOrDefault(p => p.StartsWith(shortSha, StringComparison.OrdinalIgnoreCase)) is { } parent)
        {
            ParentRequested?.Invoke(parent);
        }
    }
}
