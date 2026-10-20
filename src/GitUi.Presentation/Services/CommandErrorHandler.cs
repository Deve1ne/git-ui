using GitUi.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace GitUi.Presentation.Services;

/// <summary>
/// Single guard around every user-triggered asynchronous operation in the Presentation layer.
/// <c>[RelayCommand]</c> generates an <c>AsyncRelayCommand</c> that rethrows unhandled exceptions
/// onto the Avalonia dispatcher, which kills the process; the same is true of every
/// fire-and-forget <c>_ = SomethingAsync()</c>. Wrapping those bodies here turns a git failure
/// into a logged error plus an error dialog.
/// </summary>
public sealed class CommandErrorHandler
{
    private const string LogDirectoryHint = "see log directory in %AppData%/GitUi/logs";

    private readonly DialogService _dialogService;
    private readonly ILogger<CommandErrorHandler> _logger;

    public CommandErrorHandler(DialogService dialogService, ILogger<CommandErrorHandler> logger)
    {
        _dialogService = dialogService;
        _logger = logger;
    }

    /// <summary>
    /// Raised when a merge, pull, rebase, cherry-pick or revert stopped on conflicts. That is the
    /// normal start of a conflict resolution, not a failure: no error dialog is shown.
    /// </summary>
    public event Action<string>? MergeConflictsOccurred;

    public async Task RunGuardedAsync(Func<Task> action, string operationName)
    {
        try
        {
            await action();
        }
        catch (GitMergeConflictException ex)
        {
            _logger.LogInformation("{Operation} stopped on conflicts: {Details}", operationName, ex.Details);
            MergeConflictsOccurred?.Invoke(operationName);
        }
        catch (GitOperationException ex)
        {
            _logger.LogError(ex, "{Operation} failed: {Details}", operationName, ex.Details);
            // git's own explanation is what tells the user what to do; the command line alone does not.
            var message = string.IsNullOrWhiteSpace(ex.Details) ? ex.Message : $"{ex.Message}\n\n{ex.Details.Trim()}";
            await ReportAsync(message, operationName);
        }
        catch (InvalidOperationException ex)
        {
            // Raised by the application layer for refused operations (undo after HEAD moved,
            // deleting the checked-out branch...): the message is meant for the user.
            _logger.LogWarning(ex, "{Operation} refused", operationName);
            await ReportAsync(ex.Message, operationName);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Unexpected failure in {Operation}", operationName);
            await ReportAsync("An unexpected error occurred.", operationName);
        }
    }

    private async Task ReportAsync(string message, string operationName)
    {
        try
        {
            await _dialogService.ShowErrorAsync(message, LogDirectoryHint);
        }
        catch (Exception ex)
        {
            // The error-reporting path must never take the process down on top of the original failure.
            _logger.LogError(ex, "Failed to display the error dialog for {Operation}", operationName);
        }
    }
}
