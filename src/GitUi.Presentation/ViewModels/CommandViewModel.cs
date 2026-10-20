using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitUi.Infrastructure.Processes;
using GitUi.Presentation.Services;

namespace GitUi.Presentation.ViewModels;

public partial class CommandViewModel : ObservableObject
{
    private readonly ShellCommandRunner _runner;
    private readonly CommandErrorHandler _errorHandler;
    private string? _repositoryPath;

    [ObservableProperty]
    private string commandText = string.Empty;

    [ObservableProperty]
    private string outputText = string.Empty;

    [ObservableProperty]
    private bool isPanelVisible;

    [ObservableProperty]
    private string promptPath = string.Empty;

    public CommandViewModel(ShellCommandRunner runner, CommandErrorHandler errorHandler)
    {
        _runner = runner;
        _errorHandler = errorHandler;
    }

    public void SetRepositoryPath(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
        PromptPath = repositoryPath;
    }

    public void TogglePanel() => IsPanelVisible = !IsPanelVisible;

    [RelayCommand]
    private Task RunAsync() => _errorHandler.RunGuardedAsync(async () =>
    {
        if (_repositoryPath is null || string.IsNullOrWhiteSpace(CommandText))
        {
            return;
        }

        var result = await _runner.RunAsync(CommandText, _repositoryPath);
        OutputText = result.ExitCode == 0
            ? result.StandardOutput
            : $"{result.StandardOutput}{result.StandardError}";
        CommandText = string.Empty;
    }, "run command");

    [RelayCommand]
    private void ClearOutput() => OutputText = string.Empty;

    [RelayCommand]
    private void ClosePanel() => IsPanelVisible = false;
}
