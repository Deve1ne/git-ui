using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>A line of a conflict on one side, with its "take into the output" check box.</summary>
public partial class ConflictLineViewModel : ObservableObject
{
    public ConflictLineViewModel(int chunkIndex, int lineIndex, string text)
    {
        ChunkIndex = chunkIndex;
        LineIndex = lineIndex;
        Text = text;
    }

    public int ChunkIndex { get; }
    public int LineIndex { get; }
    public string Text { get; }

    [ObservableProperty]
    private bool isTaken;
}

/// <summary>
/// The header of one side of a conflict block: a single check box that takes (or drops) every
/// line of that side of the block, instead of ticking them one by one.
/// </summary>
public partial class ConflictBlockViewModel : ObservableObject
{
    private readonly IReadOnlyList<ConflictLineViewModel> _lines;
    private readonly Action<ConflictBlockViewModel> _toggle;

    public ConflictBlockViewModel(int chunkIndex, bool isOurs, IReadOnlyList<ConflictLineViewModel> lines, Action<ConflictBlockViewModel> toggle)
    {
        ChunkIndex = chunkIndex;
        IsOurs = isOurs;
        _lines = lines;
        _toggle = toggle;
    }

    public int ChunkIndex { get; }

    public bool IsOurs { get; }

    public bool HasLines => _lines.Count > 0;

    public bool IsTaken => _lines.Count > 0 && _lines.All(l => l.IsTaken);

    public string Label => _lines.Count == 0
        ? $"Conflict {ChunkIndex + 1} — nothing on this side"
        : $"Conflict {ChunkIndex + 1} — take this block ({_lines.Count} line{(_lines.Count == 1 ? string.Empty : "s")})";

    public void Refresh() => OnPropertyChanged(nameof(IsTaken));

    [RelayCommand]
    private void Toggle() => _toggle(this);
}

/// <summary>
/// A displayed row of the A or B pane: a merged line, a conflict line with its check box, or a
/// filler that pads the shorter side of a conflict so both panes keep the same rows face to face
/// (which is what lets them scroll together).
/// </summary>
/// <param name="LineNumber">Line number in this side's version of the file; null on fillers.</param>
/// <param name="Block">Set on the header row that starts a conflict block.</param>
public sealed record MergePaneRow(string Text, ConflictLineViewModel? Line, bool IsFiller = false, int? LineNumber = null, ConflictBlockViewModel? Block = null)
{
    public bool IsConflict => Line is not null;

    public bool IsBlockHeader => Block is not null;

    public bool IsStable => Line is null && !IsFiller && Block is null;
}

/// <summary>
/// Three-pane conflict editor modelled on GitKraken's merge tool: A (current / ours) and B
/// (incoming / theirs) on top, Output below. Ticking lines builds the output; the output can
/// also be edited by hand. Saving writes the file and marks it resolved.
/// </summary>
public partial class MergeToolViewModel : ObservableObject
{
    private readonly ConflictResolutionService _service;
    private readonly CommandErrorHandler _errorHandler;
    private readonly List<(List<ConflictLineViewModel> Ours, List<ConflictLineViewModel> Theirs)> _chunks = new();
    private readonly HashSet<int> _touchedChunks = new();
    private ConflictDocument? _document;
    private string? _repositoryPath;
    private bool _composing;

    private readonly NotificationService _notifications;

    public MergeToolViewModel(ConflictResolutionService service, CommandErrorHandler errorHandler, NotificationService notifications)
    {
        _service = service;
        _notifications = notifications;
        _errorHandler = errorHandler;
    }

    /// <summary>Raised after the file was saved, resolved or the tool was closed.</summary>
    public event Action? Closed;

    /// <summary>Asks the view to bring a conflict into view: its first row in the A/B panes, then in the output.</summary>
    public event Action<int, int>? ScrollToConflictRequested;

    /// <summary>The merged output, line by line with where each line comes from (for highlighting).</summary>
    public ObservableCollection<MergeOutputLine> OutputLines { get; } = new();

    public Func<string, string, Task<bool>>? RequestConfirmation { get; set; }

    public ObservableCollection<MergePaneRow> OursRows { get; } = new();

    public ObservableCollection<MergePaneRow> TheirsRows { get; } = new();

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string filePath = string.Empty;

    [ObservableProperty]
    private string oursHeader = "A";

    [ObservableProperty]
    private string theirsHeader = "B";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLineMerge))]
    private bool isDeleteConflict;

    [ObservableProperty]
    private string deleteConflictMessage = string.Empty;

    public bool IsLineMerge => !IsDeleteConflict;

    [ObservableProperty]
    private string outputText = string.Empty;

    [ObservableProperty]
    private bool isOutputEdited;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConflictPosition))]
    private int currentConflict;

    public int ConflictCount => _chunks.Count;

    public string ConflictPosition => ConflictCount == 0
        ? "no conflict markers left"
        : $"conflict {CurrentConflict + 1} of {ConflictCount}";

    public int UnresolvedCount => Enumerable.Range(0, _chunks.Count).Count(i => !_touchedChunks.Contains(i));

    public async Task OpenAsync(string repositoryPath, string path)
    {
        // Like GitKraken: merge the three versions again first; when no true conflict remains the
        // file is resolved on the spot and the editor does not even open.
        if (await _service.TryAutoResolveAsync(repositoryPath, path))
        {
            _notifications.Show($"{path}: the merge tool resolved this conflict automatically");
            Closed?.Invoke();
            return;
        }

        _repositoryPath = repositoryPath;
        FilePath = path;
        var (versions, document) = await _service.LoadAsync(repositoryPath, path);

        IsDeleteConflict = versions.IsDeleteConflict;
        DeleteConflictMessage = versions.IsDeleteConflict
            ? versions.HasOurs
                ? $"{path} was modified on the current branch but deleted on the incoming one."
                : $"{path} was deleted on the current branch but modified on the incoming one."
            : string.Empty;

        Build(document);
        IsOpen = true;
        if (ConflictCount > 0)
        {
            GoTo(0);
        }
    }

    private void Build(ConflictDocument? document)
    {
        _document = document;
        _chunks.Clear();
        _touchedChunks.Clear();
        OursRows.Clear();
        TheirsRows.Clear();
        OutputLines.Clear();
        IsOutputEdited = false;
        CurrentConflict = 0;

        if (document is null)
        {
            OutputText = string.Empty;
            NotifyCounts();
            return;
        }

        var firstChunk = document.Conflicts.FirstOrDefault();
        OursHeader = $"A — current ({firstChunk?.OursLabel ?? "ours"})";
        TheirsHeader = $"B — incoming ({firstChunk?.TheirsLabel ?? "theirs"})";

        // Each side is numbered as its own version of the file: A = current, B = incoming.
        var oursLine = 1;
        var theirsLine = 1;
        foreach (var segment in document.Segments)
        {
            if (segment is StableSegment stable)
            {
                foreach (var line in stable.Lines)
                {
                    OursRows.Add(new MergePaneRow(line, null, LineNumber: oursLine++));
                    TheirsRows.Add(new MergePaneRow(line, null, LineNumber: theirsLine++));
                }

                continue;
            }

            var chunk = (ConflictChunk)segment;
            var chunkIndex = _chunks.Count;
            var ours = chunk.Ours.Select((text, i) => Track(new ConflictLineViewModel(chunkIndex, i, text))).ToList();
            var theirs = chunk.Theirs.Select((text, i) => Track(new ConflictLineViewModel(chunkIndex, i, text))).ToList();
            _chunks.Add((ours, theirs));

            // One header row per side, face to face, to take a whole block at once.
            var oursBlock = new ConflictBlockViewModel(chunkIndex, isOurs: true, ours, ToggleBlock);
            var theirsBlock = new ConflictBlockViewModel(chunkIndex, isOurs: false, theirs, ToggleBlock);
            ours.ForEach(l => l.PropertyChanged += (_, _) => oursBlock.Refresh());
            theirs.ForEach(l => l.PropertyChanged += (_, _) => theirsBlock.Refresh());
            OursRows.Add(new MergePaneRow(string.Empty, null, Block: oursBlock));
            TheirsRows.Add(new MergePaneRow(string.Empty, null, Block: theirsBlock));

            // Pad the shorter side so every row of A faces the same row of B.
            var height = Math.Max(ours.Count, theirs.Count);
            for (var row = 0; row < height; row++)
            {
                OursRows.Add(row < ours.Count
                    ? new MergePaneRow(ours[row].Text, ours[row], LineNumber: oursLine++)
                    : new MergePaneRow(string.Empty, null, IsFiller: true));
                TheirsRows.Add(row < theirs.Count
                    ? new MergePaneRow(theirs[row].Text, theirs[row], LineNumber: theirsLine++)
                    : new MergePaneRow(string.Empty, null, IsFiller: true));
            }
        }

        NotifyCounts();
        Recompose();
    }

    private ConflictLineViewModel Track(ConflictLineViewModel line)
    {
        line.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConflictLineViewModel.IsTaken))
            {
                _touchedChunks.Add(line.ChunkIndex);
                NotifyCounts();
                Recompose();
            }
        };
        return line;
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(ConflictPosition));
        OnPropertyChanged(nameof(UnresolvedCount));
    }

    /// <summary>Rebuilds the output from the ticked lines, unless the user is editing it by hand.</summary>
    private void Recompose()
    {
        if (_document is null || IsOutputEdited)
        {
            return;
        }

        var resolutions = _chunks.Select((chunk, i) => _touchedChunks.Contains(i)
            ? new ConflictResolution(
                chunk.Ours.Where(l => l.IsTaken).Select(l => l.LineIndex).ToHashSet(),
                chunk.Theirs.Where(l => l.IsTaken).Select(l => l.LineIndex).ToHashSet())
            : null).ToList();

        var lines = _document.ComposeLines(resolutions);
        OutputLines.Clear();
        foreach (var line in lines)
        {
            OutputLines.Add(line);
        }

        _composing = true;
        OutputText = _document.Compose(resolutions);
        _composing = false;
    }

    partial void OnOutputTextChanged(string value)
    {
        if (!_composing)
        {
            IsOutputEdited = true;
        }
    }

    /// <summary>Switches the output from the highlighted view to a free-text editor.</summary>
    [RelayCommand]
    private void EditOutput() => IsOutputEdited = true;

    [RelayCommand]
    private void ResetOutput()
    {
        IsOutputEdited = false;
        Recompose();
    }

    [RelayCommand]
    private void NextConflict() => GoTo(Math.Min(CurrentConflict + 1, ConflictCount - 1));

    [RelayCommand]
    private void PreviousConflict() => GoTo(Math.Max(CurrentConflict - 1, 0));

    private void GoTo(int index)
    {
        if (index < 0 || index >= _chunks.Count)
        {
            return;
        }

        CurrentConflict = index;

        // Both panes have the same rows (fillers included): one row index serves both.
        var paneRow = 0;
        while (paneRow < OursRows.Count && OursRows[paneRow].Block?.ChunkIndex != index
               && OursRows[paneRow].Line?.ChunkIndex != index && TheirsRows[paneRow].Line?.ChunkIndex != index)
        {
            paneRow++;
        }

        var outputRow = 0;
        while (outputRow < OutputLines.Count && OutputLines[outputRow].ConflictIndex != index)
        {
            outputRow++;
        }

        ScrollToConflictRequested?.Invoke(paneRow, Math.Min(outputRow, Math.Max(0, OutputLines.Count - 1)));
    }

    /// <summary>A block header's check box: take every line of that side of the block, or drop them all.</summary>
    private void ToggleBlock(ConflictBlockViewModel block)
    {
        var take = !block.IsTaken;
        var (ours, theirs) = _chunks[block.ChunkIndex];
        SetBlock(block.ChunkIndex, block.IsOurs ? take : ours.All(l => l.IsTaken), block.IsOurs ? theirs.All(l => l.IsTaken) : take);
        CurrentConflict = block.ChunkIndex;
    }

    [RelayCommand]
    private void TakeCurrentOurs() => SetBlock(CurrentConflict, ours: true, theirs: false);

    [RelayCommand]
    private void TakeCurrentTheirs() => SetBlock(CurrentConflict, ours: false, theirs: true);

    /// <summary>Both sides, A first then B (the order the output writes them).</summary>
    [RelayCommand]
    private void TakeCurrentBoth() => SetBlock(CurrentConflict, ours: true, theirs: true);

    private void SetBlock(int chunkIndex, bool ours, bool theirs)
    {
        if (chunkIndex < 0 || chunkIndex >= _chunks.Count)
        {
            return;
        }

        IsOutputEdited = false;
        var (oursLines, theirsLines) = _chunks[chunkIndex];
        oursLines.ForEach(l => l.IsTaken = ours);
        theirsLines.ForEach(l => l.IsTaken = theirs);

        // A side whose boxes already had the requested value raised nothing: mark the block handled.
        _touchedChunks.Add(chunkIndex);
        NotifyCounts();
        Recompose();
    }

    [RelayCommand]
    private void TakeAllOurs() => TakeAll(ours: true, theirs: false);

    [RelayCommand]
    private void TakeAllTheirs() => TakeAll(ours: false, theirs: true);

    [RelayCommand]
    private void UntakeAll() => TakeAll(ours: false, theirs: false);

    private void TakeAll(bool ours, bool theirs)
    {
        IsOutputEdited = false;
        foreach (var (oursLines, theirsLines) in _chunks)
        {
            oursLines.ForEach(l => l.IsTaken = ours);
            theirsLines.ForEach(l => l.IsTaken = theirs);
        }

        // Setting a box to the value it already has raises nothing: mark every chunk handled explicitly.
        for (var i = 0; i < _chunks.Count; i++)
        {
            _touchedChunks.Add(i);
        }

        NotifyCounts();
        Recompose();
    }

    [RelayCommand]
    private Task SaveAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        var stillConflicted = !IsOutputEdited && UnresolvedCount > 0;
        if (stillConflicted)
        {
            var proceed = RequestConfirmation is null || await RequestConfirmation.Invoke(
                "Unresolved conflicts",
                $"{UnresolvedCount} conflict(s) have no line taken yet. Save anyway? Their conflict markers stay in the file and it stays conflicted.");
            if (!proceed)
            {
                return;
            }

            // Not staged: git keeps the file listed as conflicted until it is resolved.
            await _service.SaveResolutionAsync(_repositoryPath, FilePath, OutputText, markResolved: false);
        }
        else
        {
            await _service.SaveResolutionAsync(_repositoryPath, FilePath, OutputText);
        }

        Close();
    }, "save merge resolution");

    [RelayCommand]
    private Task TakeWholeFileAsync(ConflictSide side) => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null)
        {
            return;
        }

        await _service.TakeSideAsync(_repositoryPath, FilePath, side);
        Close();
    }, side == ConflictSide.Ours ? "take current version" : "take incoming version");

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        _document = null;
        _chunks.Clear();
        OursRows.Clear();
        TheirsRows.Clear();
        OutputLines.Clear();
        Closed?.Invoke();
    }
}
