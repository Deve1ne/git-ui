using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.DTOs;
using GitUi.Application.Services;

namespace GitUi.Presentation.ViewModels;

public partial class CommitGraphViewModel : ObservableObject
{
    private readonly CommitGraphService _commitGraphService;
    private IReadOnlyList<int> _matchIndexes = Array.Empty<int>();

    public ObservableCollection<CommitGraphRow> Rows { get; } = new();

    [ObservableProperty]
    private CommitGraphRow? selectedRow;

    /// <summary>The graph search bar (Ctrl+F) is shown.</summary>
    [ObservableProperty]
    private bool isSearchOpen;

    [ObservableProperty]
    private string searchText = string.Empty;

    /// <summary>SHAs of the rows matching <see cref="SearchText"/>; null when not searching, so the graph draws normally.</summary>
    [ObservableProperty]
    private IReadOnlySet<string>? searchMatches;

    [ObservableProperty]
    private string searchSummary = string.Empty;

    private int _currentMatch = -1;

    private readonly GraphFilterService _graphFilters;

    public CommitGraphViewModel(CommitGraphService commitGraphService, GraphFilterService graphFilters)
    {
        _commitGraphService = commitGraphService;
        _graphFilters = graphFilters;
    }

    public void Clear()
    {
        _repositoryPath = null;
        Rows.Clear();
        SelectedRow = null;
        RefreshSearch();
    }

    private string? _repositoryPath;

    public async Task LoadAsync(string repositoryPath)
    {
        var filter = await _graphFilters.GetAsync(repositoryPath);
        var graph = await _commitGraphService.GetGraphAsync(repositoryPath, filter);

        // A reload of the same repository (fetch, stage, auto-fetch...) keeps the selected commit.
        var keepSha = string.Equals(_repositoryPath, repositoryPath, StringComparison.OrdinalIgnoreCase) ? SelectedRow?.Node.Commit.Sha : null;
        _repositoryPath = repositoryPath;

        Rows.Clear();
        foreach (var row in graph)
        {
            Rows.Add(row);
        }

        SelectedRow = keepSha is null ? null : Rows.FirstOrDefault(r => r.Node.Commit.Sha == keepSha);
        RefreshSearch();
    }

    [RelayCommand]
    private void OpenSearch() => IsSearchOpen = true;

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        SearchText = string.Empty;
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshSearch();
        // Typing jumps to the first match, like a browser's find bar.
        if (_matchIndexes.Count > 0)
        {
            GoToMatch(0);
        }
    }

    [RelayCommand]
    private void NextMatch() => Step(+1);

    [RelayCommand]
    private void PreviousMatch() => Step(-1);

    private void Step(int direction)
    {
        if (_matchIndexes.Count == 0)
        {
            return;
        }

        // Continue from the selected row when the user clicked elsewhere since the last jump.
        var selectedIndex = SelectedRow is null ? -1 : Rows.IndexOf(SelectedRow);
        int next;
        if (direction > 0)
        {
            next = 0; // wraps to the first match
            for (var i = 0; i < _matchIndexes.Count; i++)
            {
                if (_matchIndexes[i] > selectedIndex)
                {
                    next = i;
                    break;
                }
            }
        }
        else
        {
            next = _matchIndexes.Count - 1; // wraps to the last match
            for (var i = _matchIndexes.Count - 1; i >= 0; i--)
            {
                if (_matchIndexes[i] < selectedIndex)
                {
                    next = i;
                    break;
                }
            }
        }

        GoToMatch(next);
    }

    private void GoToMatch(int matchIndex)
    {
        _currentMatch = matchIndex;
        SelectedRow = Rows[_matchIndexes[matchIndex]];
        UpdateSummary();
    }

    private void RefreshSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            _matchIndexes = Array.Empty<int>();
            SearchMatches = null;
        }
        else
        {
            _matchIndexes = CommitSearch.FindAll(Rows, SearchText);
            SearchMatches = _matchIndexes.Select(i => Rows[i].Node.Commit.Sha).ToHashSet();
        }

        _currentMatch = -1;
        UpdateSummary();
    }

    private void UpdateSummary() => SearchSummary = string.IsNullOrWhiteSpace(SearchText)
        ? string.Empty
        : _matchIndexes.Count == 0
            ? "No results"
            : _currentMatch < 0 ? $"{_matchIndexes.Count} results" : $"{_currentMatch + 1} of {_matchIndexes.Count}";
}
