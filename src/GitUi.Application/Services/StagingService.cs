using GitUi.Application.Abstractions;
using GitUi.Domain.Entities;

namespace GitUi.Application.Services;

public sealed class StagingService
{
    private readonly IGitRepositoryWriter _writer;

    public StagingService(IGitRepositoryWriter writer) => _writer = writer;

    public Task StageAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _writer.StageAsync(path, filePath, cancellationToken);

    public Task UnstageAsync(string path, string filePath, CancellationToken cancellationToken = default)
        => _writer.UnstageAsync(path, filePath, cancellationToken);

    public Task ApplyPatchToIndexAsync(string path, string patch, bool reverse, CancellationToken cancellationToken = default)
        => _writer.ApplyPatchToIndexAsync(path, patch, reverse, cancellationToken);

    /// <summary>Discards the unstaged changes of <paramref name="changes"/>; new (untracked) files are deleted.</summary>
    public Task DiscardAsync(string path, IEnumerable<FileChange> changes, CancellationToken cancellationToken = default)
    {
        var unstaged = changes.Where(c => !c.IsStaged && c.ChangeType != FileChangeType.Conflicted).ToList();
        var untracked = unstaged.Where(c => c.ChangeType == FileChangeType.Added).Select(c => c.Path).ToList();
        var tracked = unstaged.Where(c => c.ChangeType != FileChangeType.Added).Select(c => c.Path).ToList();
        return tracked.Count == 0 && untracked.Count == 0
            ? Task.CompletedTask
            : _writer.DiscardWorkingChangesAsync(path, tracked, untracked, cancellationToken);
    }

    /// <summary>Removes a working-tree change described by <paramref name="patch"/> (built for a reverse apply).</summary>
    public Task DiscardPatchAsync(string path, string patch, CancellationToken cancellationToken = default)
        => _writer.ApplyPatchToWorkingTreeAsync(path, patch, reverse: true, cancellationToken);

    public Task CommitAsync(string path, string message, CancellationToken cancellationToken = default)
        => CommitAsync(path, message, GitCommitOptions.Default, cancellationToken);

    public Task CommitAsync(string path, string message, GitCommitOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Commit message cannot be empty.");
        }

        return options == GitCommitOptions.Default
            ? _writer.CommitAsync(path, message, cancellationToken)
            : _writer.CommitAsync(path, message, options, cancellationToken);
    }
}
