using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Domain.Entities;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// GitKraken's file view, shown in place of the graph: the file's history (the commits that changed
/// it, with the diff of the selected one) or its blame (who last changed each line).
/// </summary>
public partial class FileHistoryViewModel : ObservableObject
{
    private readonly IGitRepositoryReader _reader;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;
    private int _generation;

    public FileHistoryViewModel(IGitRepositoryReader reader, CommandErrorHandler errorHandler, DiffSettings diffSettings)
    {
        _reader = reader;
        _errorHandler = errorHandler;
        Diff = new DiffPresenter(diffSettings);
    }

    /// <summary>The selected commit's diff of the file (unified or split, per the shared diff options).</summary>
    public DiffPresenter Diff { get; }

    public ObservableCollection<FileHistoryEntry> Entries { get; } = new();

    public ObservableCollection<DiffRow> DiffRows => Diff.Rows;

    public ObservableCollection<BlameLine> BlameLines { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen))]
    private string? filePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHistoryMode), nameof(Title))]
    private bool isBlameMode;

    [ObservableProperty]
    private FileHistoryEntry? selectedEntry;

    public bool IsOpen => FilePath is not null;

    public bool IsHistoryMode => !IsBlameMode;

    public string Title => IsBlameMode ? "BLAME" : "HISTORY";

    /// <summary>Raised when the user asks to see a commit of the history in the graph.</summary>
    public event Action<string>? CommitRequested;

    /// <summary>Called on every reload: only another repository (or none) closes the file view.</summary>
    public void SetRepositoryPath(string? repositoryPath)
    {
        if (string.Equals(_repositoryPath, repositoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _repositoryPath = repositoryPath;
        Close();
    }

    public Task OpenHistoryAsync(string filePath) => OpenAsync(filePath, blame: false);

    public Task OpenBlameAsync(string filePath) => OpenAsync(filePath, blame: true);

    private Task OpenAsync(string path, bool blame) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is not { } repository)
        {
            return;
        }

        FilePath = path.Replace('\\', '/');
        IsBlameMode = blame;
        await LoadAsync(repository);
    }, blame ? "load file blame" : "load file history");

    private async Task LoadAsync(string repository)
    {
        var generation = ++_generation;
        var file = FilePath!;
        if (IsBlameMode)
        {
            var lines = await _reader.GetBlameAsync(repository, file);
            if (generation != _generation)
            {
                return;
            }

            BlameLines.Clear();
            foreach (var line in lines)
            {
                BlameLines.Add(line);
            }
        }
        else
        {
            var entries = await _reader.GetFileHistoryAsync(repository, file);
            if (generation != _generation)
            {
                return;
            }

            Entries.Clear();
            foreach (var entry in entries)
            {
                Entries.Add(entry);
            }

            SelectedEntry = Entries.FirstOrDefault();
        }
    }

    partial void OnSelectedEntryChanged(FileHistoryEntry? value)
    {
        Diff.Clear();
        if (value is null || _repositoryPath is not { } repository)
        {
            return;
        }

        _ = _errorHandler.RunGuardedAsync(async () =>
        {
            var hunks = await _reader.GetCommitFileDiffAsync(repository, value.Commit.Sha, value.Path);
            if (!ReferenceEquals(value, SelectedEntry))
            {
                return;
            }

            Diff.Show(hunks);
        }, "load file diff");
    }

    [RelayCommand]
    private Task ShowHistoryAsync() => FilePath is { } file ? OpenHistoryAsync(file) : Task.CompletedTask;

    [RelayCommand]
    private Task ShowBlameAsync() => FilePath is { } file ? OpenBlameAsync(file) : Task.CompletedTask;

    [RelayCommand]
    private void ShowCommit(string sha) => CommitRequested?.Invoke(sha);

    [RelayCommand]
    public void Close()
    {
        _generation++;
        FilePath = null;
        SelectedEntry = null;
        Entries.Clear();
        BlameLines.Clear();
        Diff.Clear();
    }
}
