using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Application.Services;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

/// <summary>Toolbar Undo/Redo over the operations journaled by <see cref="UndoService"/>.</summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly UndoService _undoService;
    private readonly RepositoryChangeNotifier _changeNotifier;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;

    public HistoryViewModel(UndoService undoService, RepositoryChangeNotifier changeNotifier, CommandErrorHandler errorHandler)
    {
        _undoService = undoService;
        _changeNotifier = changeNotifier;
        _errorHandler = errorHandler;
        _undoService.JournalChanged += path =>
        {
            if (string.Equals(path, _repositoryPath, StringComparison.OrdinalIgnoreCase))
            {
                Refresh();
            }
        };
    }

    public string UndoTooltip => _repositoryPath is not null && _undoService.PeekUndo(_repositoryPath) is { } operation
        ? $"Undo {operation.Description}"
        : "Nothing to undo";

    public string RedoTooltip => _repositoryPath is not null && _undoService.PeekRedo(_repositoryPath) is { } operation
        ? $"Redo {operation.Description}"
        : "Nothing to redo";

    public void SetRepositoryPath(string? repositoryPath)
    {
        _repositoryPath = repositoryPath;
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(UndoTooltip));
        OnPropertyChanged(nameof(RedoTooltip));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo() => _repositoryPath is not null && _undoService.PeekUndo(_repositoryPath) is not null;

    private bool CanRedo() => _repositoryPath is not null && _undoService.PeekRedo(_repositoryPath) is not null;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        await _undoService.UndoAsync(_repositoryPath!);
        _changeNotifier.NotifyChanged();
    }, "undo");

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private Task RedoAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        await _undoService.RedoAsync(_repositoryPath!);
        _changeNotifier.NotifyChanged();
    }, "redo");
}
