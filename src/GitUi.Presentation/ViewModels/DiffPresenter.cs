using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitUi.Application.Abstractions;
using GitUi.Application.Services;
using GitUi.Domain.ValueObjects;

namespace GitUi.Presentation.ViewModels;

/// <summary>
/// Diff display options shared by every diff in the app (GitKraken keeps them global): split
/// view and ignore whitespace, saved in the settings file.
/// </summary>
public partial class DiffSettings : ObservableObject
{
    private readonly ISettingsStore _settingsStore;
    private bool _loading;

    public DiffSettings(ISettingsStore settingsStore) => _settingsStore = settingsStore;

    [ObservableProperty]
    private bool isSplit;

    [ObservableProperty]
    private bool ignoreWhitespace;

    /// <summary>Changed-file lists as a folder tree ("Tree") rather than full paths ("Path").</summary>
    [ObservableProperty]
    private bool filesAsTree;

    public async Task LoadAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        _loading = true;
        IsSplit = settings.DiffSplitView ?? false;
        IgnoreWhitespace = settings.DiffIgnoreWhitespace ?? false;
        FilesAsTree = settings.FilesAsTree ?? false;
        _loading = false;
    }

    partial void OnIsSplitChanged(bool value) => Save();

    partial void OnIgnoreWhitespaceChanged(bool value) => Save();

    partial void OnFilesAsTreeChanged(bool value) => Save();

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        var split = IsSplit;
        var whitespace = IgnoreWhitespace;
        var tree = FilesAsTree;
        _ = _settingsStore.UpdateAsync(s => s with { DiffSplitView = split, DiffIgnoreWhitespace = whitespace, FilesAsTree = tree });
    }
}

/// <summary>
/// Turns one file's hunks into the rows a diff view shows — unified or side by side, with or
/// without whitespace-only changes — and rebuilds them when the shared options change.
/// </summary>
public sealed partial class DiffPresenter : ObservableObject
{
    private IReadOnlyList<DiffHunk> _hunks = Array.Empty<DiffHunk>();

    public DiffPresenter(DiffSettings settings)
    {
        Settings = settings;
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffSettings.FilesAsTree))
            {
                return;
            }

            Rebuild();
            OnPropertyChanged(nameof(ShowsRealHunks));
        };
    }

    public DiffSettings Settings { get; }

    public ObservableCollection<DiffRow> Rows { get; } = new();

    public ObservableCollection<SideBySideRow> SplitRows { get; } = new();

    /// <summary>The unified rows map 1:1 onto git's hunks, so hunk / line staging can use them.</summary>
    public bool ShowsRealHunks => !Settings.IsSplit && !Settings.IgnoreWhitespace;

    /// <summary>The hunks as git produced them (what patches are built from).</summary>
    public IReadOnlyList<DiffHunk> Hunks => _hunks;

    public void Show(IReadOnlyList<DiffHunk> hunks)
    {
        _hunks = hunks;
        Rebuild();
    }

    public void Clear() => Show(Array.Empty<DiffHunk>());

    private void Rebuild()
    {
        var shown = Settings.IgnoreWhitespace ? DiffLayout.IgnoreWhitespace(_hunks) : _hunks;

        Rows.Clear();
        SplitRows.Clear();
        if (Settings.IsSplit)
        {
            foreach (var row in DiffLayout.SideBySide(shown))
            {
                SplitRows.Add(row);
            }
        }
        else
        {
            foreach (var row in DiffRow.FromHunks(shown))
            {
                Rows.Add(row);
            }
        }
    }
}
