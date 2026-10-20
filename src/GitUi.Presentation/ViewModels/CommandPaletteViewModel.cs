using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>One entry of the command palette.</summary>
/// <param name="Category">Shown dimmed before the label ("Branch", "Repository"...).</param>
public sealed record PaletteCommand(string Category, string Label, string? Shortcut, Action Run)
{
    public string SearchText => $"{Category} {Label}";
}

/// <summary>
/// GitKraken's command palette (Ctrl+P / Ctrl+Shift+P): type to filter every action, the local
/// branches to check out and the recent repositories, Enter to run.
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private IReadOnlyList<PaletteCommand> _all = Array.Empty<PaletteCommand>();

    /// <summary>Builds the current list (branches and repositories change), called on every opening.</summary>
    public Func<IReadOnlyList<PaletteCommand>>? CommandsProvider { get; set; }

    public ObservableCollection<PaletteCommand> Results { get; } = new();

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string query = string.Empty;

    [ObservableProperty]
    private PaletteCommand? selected;

    public void Open()
    {
        _all = CommandsProvider?.Invoke() ?? Array.Empty<PaletteCommand>();
        Query = string.Empty;
        Refresh();
        IsOpen = true;
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        Results.Clear();
        foreach (var command in FuzzyMatcher.Filter(_all, c => c.SearchText, Query).Take(60))
        {
            Results.Add(command);
        }

        Selected = Results.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectNext() => MoveSelection(+1);

    [RelayCommand]
    private void SelectPrevious() => MoveSelection(-1);

    private void MoveSelection(int step)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[(index + step + Results.Count) % Results.Count];
    }

    [RelayCommand]
    private void Execute(PaletteCommand? command)
    {
        var chosen = command ?? Selected;
        Close();
        chosen?.Run();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Query = string.Empty;
    }
}
