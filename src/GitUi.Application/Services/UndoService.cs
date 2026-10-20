using GitUi.Application.Abstractions;

namespace GitUi.Application.Services;

public enum UndoableOperationKind { Commit, Checkout, CreateBranch, DeleteBranch, DiscardAll }

/// <summary>
/// One reversible git operation. <see cref="Before"/> and <see cref="After"/> are commit SHAs for a
/// commit, branch names for a checkout, and the branch tip for branch create/delete. For a
/// discard-all, <see cref="Before"/> is the stash commit holding the discarded changes.
/// </summary>
public sealed record UndoableOperation(UndoableOperationKind Kind, string Description, string? BranchName, string? Before, string? After);

/// <summary>
/// Runs the operations the toolbar can undo/redo and journals them per repository. Only
/// operations performed through this service are journaled; anything done outside the app (or
/// in the terminal panel) is invisible to it, which is why undoing a commit checks HEAD first.
/// </summary>
public sealed class UndoService
{
    private readonly IGitRepositoryReader _reader;
    private readonly IGitRepositoryWriter _writer;
    private readonly StagingService _stagingService;
    private readonly BranchService _branchService;
    private readonly Dictionary<string, (Stack<UndoableOperation> Undo, Stack<UndoableOperation> Redo)> _journals = new(StringComparer.OrdinalIgnoreCase);

    public UndoService(IGitRepositoryReader reader, IGitRepositoryWriter writer, StagingService stagingService, BranchService branchService)
    {
        _reader = reader;
        _writer = writer;
        _stagingService = stagingService;
        _branchService = branchService;
    }

    public event Action<string>? JournalChanged;

    public UndoableOperation? PeekUndo(string path) => Journal(path).Undo.TryPeek(out var operation) ? operation : null;

    public UndoableOperation? PeekRedo(string path) => Journal(path).Redo.TryPeek(out var operation) ? operation : null;

    public Task CommitAsync(string path, string message, CancellationToken cancellationToken = default)
        => CommitAsync(path, message, GitCommitOptions.Default, cancellationToken);

    public async Task CommitAsync(string path, string message, GitCommitOptions options, CancellationToken cancellationToken = default)
    {
        var before = (await _reader.GetRepositoryAsync(path, cancellationToken)).HeadSha;
        await _stagingService.CommitAsync(path, message, options, cancellationToken);
        var after = (await _reader.GetRepositoryAsync(path, cancellationToken)).HeadSha;

        // The very first commit of a repository has no parent to reset to: it is not undoable.
        // An amend is: the soft reset goes back to the commit it replaced.
        if (before is not null && after is not null)
        {
            var verb = options.Amend ? "amend" : "commit";
            Record(path, new UndoableOperation(UndoableOperationKind.Commit, $"{verb} \"{FirstLine(message)}\"", null, before, after));
        }
    }

    public async Task CheckoutAsync(string path, string branchName, CancellationToken cancellationToken = default)
    {
        var before = (await _reader.GetRepositoryAsync(path, cancellationToken)).CurrentBranch;
        await _branchService.CheckoutAsync(path, branchName, cancellationToken);
        Record(path, new UndoableOperation(UndoableOperationKind.Checkout, $"checkout {branchName}", branchName, before, branchName));
    }

    public async Task CreateBranchAsync(string path, string branchName, string? startPoint = null, CancellationToken cancellationToken = default)
    {
        if (startPoint is null)
        {
            await _branchService.CreateBranchAsync(path, branchName, cancellationToken);
        }
        else
        {
            await _writer.CreateBranchAsync(path, branchName, startPoint, cancellationToken);
        }

        var tip = await BranchTipAsync(path, branchName, cancellationToken);
        Record(path, new UndoableOperation(UndoableOperationKind.CreateBranch, $"create branch {branchName}", branchName, null, tip));
    }

    public async Task DeleteBranchAsync(string path, string branchName, bool force, CancellationToken cancellationToken = default)
    {
        var tip = await BranchTipAsync(path, branchName, cancellationToken);
        await _branchService.DeleteBranchAsync(path, branchName, force, cancellationToken);
        Record(path, new UndoableOperation(UndoableOperationKind.DeleteBranch, $"delete branch {branchName}", branchName, tip, null));
    }

    /// <summary>Discards every change, staged or not, keeping them recoverable by Undo; false when there was nothing to discard.</summary>
    public async Task<bool> DiscardAllAsync(string path, CancellationToken cancellationToken = default)
    {
        var stashSha = await _writer.DiscardAllAsync(path, cancellationToken);
        if (stashSha is null)
        {
            return false;
        }

        Record(path, new UndoableOperation(UndoableOperationKind.DiscardAll, "discard all changes", null, stashSha, null));
        return true;
    }

    public async Task<UndoableOperation?> UndoAsync(string path, CancellationToken cancellationToken = default)
    {
        var journal = Journal(path);
        if (!journal.Undo.TryPeek(out var operation))
        {
            return null;
        }

        switch (operation.Kind)
        {
            case UndoableOperationKind.Commit:
                await EnsureHeadAsync(path, operation.After!, "undo", cancellationToken);
                // Soft reset: the commit disappears, its changes stay staged — like GitKraken's undo.
                await _writer.ResetAsync(path, operation.Before!, GitResetMode.Soft, cancellationToken);
                break;
            case UndoableOperationKind.Checkout:
                await _writer.CheckoutAsync(path, operation.Before!, cancellationToken);
                break;
            case UndoableOperationKind.CreateBranch:
                await _writer.DeleteBranchAsync(path, operation.BranchName!, force: true, cancellationToken);
                break;
            case UndoableOperationKind.DeleteBranch:
                await _writer.CreateBranchAsync(path, operation.BranchName!, operation.Before, cancellationToken);
                break;
            case UndoableOperationKind.DiscardAll:
                await _writer.RestoreDiscardedAsync(path, operation.Before!, cancellationToken);
                break;
        }

        journal.Undo.Pop();
        journal.Redo.Push(operation);
        JournalChanged?.Invoke(path);
        return operation;
    }

    public async Task<UndoableOperation?> RedoAsync(string path, CancellationToken cancellationToken = default)
    {
        var journal = Journal(path);
        if (!journal.Redo.TryPeek(out var operation))
        {
            return null;
        }

        switch (operation.Kind)
        {
            case UndoableOperationKind.Commit:
                await EnsureHeadAsync(path, operation.Before!, "redo", cancellationToken);
                await _writer.ResetAsync(path, operation.After!, GitResetMode.Soft, cancellationToken);
                break;
            case UndoableOperationKind.Checkout:
                await _writer.CheckoutAsync(path, operation.After!, cancellationToken);
                break;
            case UndoableOperationKind.CreateBranch:
                await _writer.CreateBranchAsync(path, operation.BranchName!, operation.After, cancellationToken);
                break;
            case UndoableOperationKind.DeleteBranch:
                await _writer.DeleteBranchAsync(path, operation.BranchName!, force: true, cancellationToken);
                break;
            case UndoableOperationKind.DiscardAll:
                // A new discard makes a new stash commit: the next undo must restore that one.
                var stashSha = await _writer.DiscardAllAsync(path, cancellationToken)
                    ?? throw new InvalidOperationException("Cannot redo: there are no changes to discard.");
                operation = operation with { Before = stashSha };
                break;
        }

        journal.Redo.Pop();
        journal.Undo.Push(operation);
        JournalChanged?.Invoke(path);
        return operation;
    }

    private void Record(string path, UndoableOperation operation)
    {
        var journal = Journal(path);
        journal.Undo.Push(operation);
        journal.Redo.Clear();
        JournalChanged?.Invoke(path);
    }

    private (Stack<UndoableOperation> Undo, Stack<UndoableOperation> Redo) Journal(string path)
    {
        if (!_journals.TryGetValue(path, out var journal))
        {
            journal = (new Stack<UndoableOperation>(), new Stack<UndoableOperation>());
            _journals[path] = journal;
        }

        return journal;
    }

    private async Task EnsureHeadAsync(string path, string expectedSha, string action, CancellationToken cancellationToken)
    {
        var head = (await _reader.GetRepositoryAsync(path, cancellationToken)).HeadSha;
        if (!string.Equals(head, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot {action}: HEAD moved since the commit was made.");
        }
    }

    private async Task<string?> BranchTipAsync(string path, string branchName, CancellationToken cancellationToken)
    {
        var branches = await _reader.GetBranchesAsync(path, cancellationToken);
        return branches.FirstOrDefault(b => b.IsLocal && b.Name == branchName)?.TipSha;
    }

    private static string FirstLine(string message)
    {
        var newline = message.IndexOfAny(new[] { '\r', '\n' });
        return newline < 0 ? message : message[..newline];
    }
}
